using System.Text.Json;
using Parley.Core;
using Parley.Ipc;
using Umbra.Parley;
using Xunit;

namespace Parley.Tests;

public class ParleyConnectionTests
{
    private string? answer;
    private bool throws;
    private int asked;

    private ParleyConnection NewConnection() => new(() =>
    {
        asked++;
        if (throws) throw new InvalidOperationException("IPC gate is not ready.");
        return answer;
    });

    private static string Status(int tells, long revision = 1) => JsonSerializer.Serialize(
        new StatusSnapshot { Version = ParleyIpc.Version, Revision = revision, LoggedIn = true, Tells = tells },
        ParleyIpcJson.Default.StatusSnapshot);

    [Fact]
    public void Before_parley_answers_there_is_no_status_and_a_reason_why()
    {
        var connection = NewConnection();

        connection.Refresh();

        Assert.Null(connection.Snapshot);
        Assert.Equal(ParleyConnection.NotRunning, connection.UnavailableReason);
    }

    [Fact]
    public void A_status_from_parley_is_read()
    {
        var connection = NewConnection();
        answer = Status(tells: 3);

        connection.Refresh();

        Assert.NotNull(connection.Snapshot);
        Assert.Equal(3, connection.Snapshot.Tells);
    }

    [Fact]
    public void The_same_answer_twice_is_not_parsed_twice()
    {
        var connection = NewConnection();
        answer = Status(tells: 3);
        connection.Refresh();
        var first = connection.Snapshot;
        var generation = connection.Generation;

        connection.Refresh();
        connection.Refresh();

        Assert.Equal(3, asked);
        Assert.Same(first, connection.Snapshot);
        Assert.Equal(generation, connection.Generation);
    }

    [Fact]
    public void A_changed_answer_replaces_the_status_and_moves_the_generation()
    {
        var connection = NewConnection();
        answer = Status(tells: 3);
        connection.Refresh();
        var generation = connection.Generation;

        answer = Status(tells: 0, revision: 2);
        connection.Refresh();

        Assert.Equal(0, connection.Snapshot!.Tells);
        Assert.NotEqual(generation, connection.Generation);
    }

    [Fact]
    public void Parley_going_away_clears_what_was_showing()
    {
        var connection = NewConnection();
        answer = Status(tells: 3);
        connection.Refresh();
        var generation = connection.Generation;

        throws = true;
        connection.Refresh();

        Assert.Null(connection.Snapshot);
        Assert.Equal(ParleyConnection.NotRunning, connection.UnavailableReason);
        Assert.NotEqual(generation, connection.Generation);
    }

    [Fact]
    public void Parley_coming_back_with_the_same_answer_as_before_is_still_noticed()
    {
        var connection = NewConnection();
        answer = Status(tells: 3);
        connection.Refresh();

        var status = answer;
        answer = null;
        connection.Refresh();
        Assert.Null(connection.Snapshot);

        answer = status;
        connection.Refresh();

        Assert.NotNull(connection.Snapshot);
        Assert.Equal(3, connection.Snapshot.Tells);
    }

    [Fact]
    public void Staying_unavailable_does_not_keep_moving_the_generation()
    {
        var connection = NewConnection();
        connection.Refresh();
        var generation = connection.Generation;

        connection.Refresh();
        connection.Refresh();

        Assert.Equal(generation, connection.Generation);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":2,\"tells\":5,\"loggedIn\":true}")]
    [InlineData("not json at all")]
    [InlineData("null")]
    public void A_status_this_widget_cannot_trust_is_reported_as_a_version_mismatch(string json)
    {
        var connection = NewConnection();
        answer = json;

        connection.Refresh();

        Assert.Null(connection.Snapshot);
        Assert.Equal(ParleyConnection.VersionMismatch, connection.UnavailableReason);
    }

    [Fact]
    public void What_the_store_publishes_is_what_the_widget_reads()
    {
        // The two ends of the contract, joined the way they are in game.
        var store = new ConversationStore(null, () => 1_700_000_000_000);
        store.LoadCharacter(1, "Me Myself", "Gilgamesh", saveHistory: false);
        var alice = store.OpenTell("Alice Smith", 63, "Gilgamesh");
        store.Add(alice, new ChatMessage { Timestamp = 5, Sender = "Alice Smith", SenderWorld = 63, Text = "are you around?" });
        var hunts = store.GetLinkshell(ChannelGroup.Linkshell, "Hunts", 2);
        store.Add(hunts, new ChatMessage { Timestamp = 6, Sender = "Bob Jones", SenderWorld = 63, Text = "S rank" });

        var connection = new ParleyConnection(
            () => JsonSerializer.Serialize(store.BuildStatus(windowOpen: false), ParleyIpcJson.Default.StatusSnapshot));
        connection.Refresh();
        var display = WidgetPresentation.Create(connection.Snapshot, new WidgetOptions(), connection.UnavailableReason);

        Assert.Equal("1 Tell, 1 LS", display.Text);
        Assert.Equal(2, display.Unread);
        Assert.Contains("Tell · Alice Smith@Gilgamesh (1)", display.Tooltip);
        Assert.Contains("are you around?", display.Tooltip);
        Assert.Contains("LS · Hunts (1)", display.Tooltip);
        Assert.Contains("Bob Jones: S rank", display.Tooltip);

        store.MarkAllRead();
        connection.Refresh();
        display = WidgetPresentation.Create(connection.Snapshot, new WidgetOptions(), connection.UnavailableReason);

        Assert.Equal("Chat", display.Text);
        Assert.Equal(0, display.Unread);
    }
}

public class UnreadSummaryTests
{
    [Theory]
    [InlineData(0, 0, 0, "")]
    [InlineData(1, 0, 0, "1 Tell")]
    [InlineData(4, 0, 0, "4 Tells")]
    [InlineData(0, 7, 0, "7 LS")]
    [InlineData(0, 0, 2, "2 CWLS")]
    [InlineData(2, 5, 1, "2 Tells, 5 LS, 1 CWLS")]
    public void Describes_only_what_is_unread(int tells, int linkshells, int crossWorld, string expected)
    {
        Assert.Equal(expected, UnreadSummary.Describe(tells, linkshells, crossWorld));
    }
}
