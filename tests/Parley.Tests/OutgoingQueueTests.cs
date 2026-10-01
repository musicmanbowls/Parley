using System.Text;
using Parley.Core;
using Xunit;

namespace Parley.Tests;

public class OutgoingQueueTests
{
    private readonly List<string> sent = [];
    private readonly List<(Conversation Conversation, string Text, string Reason)> failures = [];
    private long now = 10_000;
    private bool accept = true;

    private OutgoingQueue NewQueue()
    {
        var queue = new OutgoingQueue(line =>
        {
            if (!accept) return false;
            sent.Add(line);
            return true;
        }, () => now);
        queue.Failed += (conversation, text, reason) => failures.Add((conversation, text, reason));
        return queue;
    }

    /// <summary>Queues a message and runs the tick that would follow it in game.</summary>
    private static SendOutcome SendNow(OutgoingQueue queue, Conversation conversation, string text)
    {
        var outcome = queue.Send(conversation, text);
        queue.Update();
        return outcome;
    }

    private static Conversation Tell() =>
        new("t:Alice Smith@63", ChannelGroup.Tell, "Alice Smith") { WorldId = 63, WorldName = "Gilgamesh" };

    private static Conversation Shell(ChannelGroup group, int slot) =>
        new($"x:Hunts{(int)group}", group, "Hunts") { Slot = slot };

    private static string Words(int count) => string.Join(' ', Enumerable.Range(0, count).Select(i => $"word{i:000}"));

    [Fact]
    public void A_tell_is_addressed_by_name_and_world()
    {
        var queue = NewQueue();

        Assert.Equal(SendOutcome.Sent, SendNow(queue, Tell(), "  hello there  "));

        Assert.Equal(["/tell Alice Smith@Gilgamesh hello there"], sent);
    }

    [Fact]
    public void Nothing_reaches_the_game_until_the_next_tick()
    {
        var queue = NewQueue();

        queue.Send(Tell(), "hello");
        Assert.Empty(sent);

        queue.Update();
        Assert.Single(sent);
    }

    [Fact]
    public void Linkshells_use_the_command_for_their_slot()
    {
        var queue = NewQueue();

        SendNow(queue, Shell(ChannelGroup.Linkshell, 3), "hi");
        now += 1000;
        SendNow(queue, Shell(ChannelGroup.CrossWorld, 2), "hi");

        Assert.Equal(["/linkshell3 hi", "/cwlinkshell2 hi"], sent);
    }

    [Fact]
    public void Text_that_starts_with_a_slash_stays_inside_the_message()
    {
        var queue = NewQueue();

        SendNow(queue, Tell(), "/shout not a command");

        Assert.Equal(["/tell Alice Smith@Gilgamesh /shout not a command"], sent);
    }

    [Fact]
    public void Line_breaks_cannot_smuggle_in_a_second_line()
    {
        var queue = NewQueue();

        SendNow(queue, Tell(), "first\n/shout second");

        var line = Assert.Single(sent);
        Assert.DoesNotContain('\n', line);
        Assert.Equal("/tell Alice Smith@Gilgamesh first /shout second", line);
    }

    [Fact]
    public void Nothing_but_whitespace_is_not_sent()
    {
        var queue = NewQueue();

        Assert.Equal(SendOutcome.Empty, SendNow(queue, Tell(), " \r\n\t "));
        Assert.Empty(sent);
    }

    [Fact]
    public void A_linkshell_the_character_has_left_cannot_be_written_to()
    {
        var queue = NewQueue();

        Assert.Equal(SendOutcome.CannotSend, SendNow(queue, Shell(ChannelGroup.Linkshell, 0), "hi"));
        Assert.Empty(sent);
    }

    [Fact]
    public void A_tell_with_no_known_world_cannot_be_addressed()
    {
        var queue = NewQueue();
        var nowhere = new Conversation("t:Alice Smith@0", ChannelGroup.Tell, "Alice Smith");

        Assert.Equal(SendOutcome.CannotSend, SendNow(queue, nowhere, "hi"));
        Assert.Empty(sent);
    }

    [Fact]
    public void The_budget_is_what_is_left_after_the_command_and_target()
    {
        Assert.Equal(500 - "/tell Alice Smith@Gilgamesh ".Length, OutgoingQueue.Budget(Tell()));
        Assert.Equal(500 - "/linkshell3 ".Length, OutgoingQueue.Budget(Shell(ChannelGroup.Linkshell, 3)));
    }

    [Fact]
    public void A_long_message_goes_out_in_parts_spaced_apart()
    {
        var queue = NewQueue();
        queue.SplitDelayMs = 1000;
        var text = Words(150);   // about 1200 bytes

        Assert.Equal(SendOutcome.Sent, SendNow(queue, Tell(), text));
        Assert.Single(sent);

        now += 999;
        queue.Update();
        Assert.Single(sent);

        now += 1;
        queue.Update();
        Assert.Equal(2, sent.Count);

        now += 1000;
        queue.Update();
        Assert.Equal(3, sent.Count);
        Assert.Equal(0, queue.QueuedCount);

        const string prefix = "/tell Alice Smith@Gilgamesh ";
        Assert.All(sent, line =>
        {
            Assert.StartsWith(prefix, line);
            Assert.True(Encoding.UTF8.GetByteCount(line) <= OutgoingQueue.MaxLineBytes);
        });
        Assert.Equal(text, string.Join(' ', sent.Select(line => line[prefix.Length..])));
    }

    [Fact]
    public void With_splitting_off_a_long_message_is_refused_whole()
    {
        var queue = NewQueue();
        queue.SplitLongMessages = false;

        Assert.Equal(SendOutcome.TooLong, SendNow(queue, Tell(), Words(150)));
        Assert.Empty(sent);
        Assert.Equal(0, queue.QueuedCount);
    }

    [Fact]
    public void A_wall_of_text_is_refused_even_with_splitting_on()
    {
        var queue = NewQueue();

        Assert.Equal(SendOutcome.TooLong, SendNow(queue, Tell(), Words(800)));
        Assert.Empty(sent);
    }

    [Fact]
    public void Two_quick_messages_are_kept_a_beat_apart()
    {
        var queue = NewQueue();

        queue.Send(Tell(), "one");
        queue.Send(Tell(), "two");
        queue.Update();
        queue.Update();
        Assert.Equal(["/tell Alice Smith@Gilgamesh one"], sent);

        now += 349;
        queue.Update();
        Assert.Single(sent);

        now += 1;
        queue.Update();
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void An_error_line_while_a_send_is_unanswered_is_taken_as_its_failure()
    {
        var queue = NewQueue();
        var tell = Tell();
        SendNow(queue, tell, "are you there?");

        now += 300;
        Assert.True(queue.OnGameError("Alice Smith is currently offline."));

        var failure = Assert.Single(failures);
        Assert.Same(tell, failure.Conversation);
        Assert.Equal("are you there?", failure.Text);
        Assert.Equal("Alice Smith is currently offline.", failure.Reason);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Once_the_echo_has_arrived_a_later_error_is_about_something_else()
    {
        var queue = NewQueue();
        var tell = Tell();
        SendNow(queue, tell, "hello");

        queue.OnEcho(tell);
        now += 300;

        Assert.False(queue.OnGameError("You cannot use that action yet."));
        Assert.Empty(failures);
    }

    [Fact]
    public void An_error_with_nothing_sent_is_none_of_our_business()
    {
        var queue = NewQueue();

        Assert.False(queue.OnGameError("Target is not in range."));
        Assert.Empty(failures);
    }

    [Fact]
    public void An_error_long_after_a_send_is_not_blamed_on_it()
    {
        var queue = NewQueue();
        SendNow(queue, Tell(), "hello");

        now += 4001;

        Assert.False(queue.OnGameError("Unrelated."));
        Assert.Empty(failures);
    }

    [Fact]
    public void An_echo_for_another_conversation_does_not_answer_this_one()
    {
        var queue = NewQueue();
        var tell = Tell();
        SendNow(queue, tell, "hello");

        queue.OnEcho(Shell(ChannelGroup.Linkshell, 1));
        now += 100;

        Assert.True(queue.OnGameError("Message could not be sent."));
        Assert.Same(tell, Assert.Single(failures).Conversation);
    }

    [Fact]
    public void When_a_split_message_fails_everything_not_yet_delivered_is_handed_back()
    {
        var queue = NewQueue();
        var tell = Tell();
        var text = Words(150);
        SendNow(queue, tell, text);
        Assert.Single(sent);
        Assert.Equal(2, queue.QueuedCount);

        now += 200;
        queue.OnGameError("Message could not be sent.");

        Assert.Equal(text, Assert.Single(failures).Text);
        Assert.Equal(0, queue.QueuedCount);

        now += 60_000;
        queue.Update();
        Assert.Single(sent);
    }

    [Fact]
    public void A_line_the_game_will_not_take_is_reported_on_the_tick_it_was_tried()
    {
        var queue = NewQueue();
        accept = false;

        Assert.Equal(SendOutcome.Sent, queue.Send(Tell(), "hello"));
        Assert.Empty(failures);

        queue.Update();

        var failure = Assert.Single(failures);
        Assert.Equal("hello", failure.Text);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Leaving_the_linkshell_mid_message_stops_the_rest_of_it()
    {
        var queue = NewQueue();
        queue.SplitDelayMs = 1000;
        var shell = Shell(ChannelGroup.Linkshell, 2);
        SendNow(queue, shell, Words(150));
        Assert.Single(sent);

        shell.Slot = 0;
        now += 1000;
        queue.Update();

        Assert.Single(sent);
        Assert.Single(failures);
        Assert.Equal(0, queue.QueuedCount);
    }

    [Fact]
    public void Characters_the_game_cannot_send_are_removed_before_measuring()
    {
        var queue = NewQueue();
        queue.Sanitise = text => text.Replace("☃", string.Empty);

        SendNow(queue, Tell(), "cold ☃ today");
        Assert.Equal(SendOutcome.Empty, SendNow(queue, Tell(), "☃"));

        Assert.Equal(["/tell Alice Smith@Gilgamesh cold  today"], sent);
    }

    [Fact]
    public void A_send_that_is_never_answered_is_eventually_forgotten()
    {
        var queue = NewQueue();
        SendNow(queue, Tell(), "hello");
        Assert.Equal(1, queue.PendingCount);

        now += 10_001;
        queue.Update();

        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Clearing_drops_everything_still_waiting()
    {
        var queue = NewQueue();
        SendNow(queue, Tell(), Words(150));

        queue.Clear();
        now += 60_000;
        queue.Update();

        Assert.Single(sent);
        Assert.Equal(0, queue.QueuedCount);
        Assert.Equal(0, queue.PendingCount);
    }
}
