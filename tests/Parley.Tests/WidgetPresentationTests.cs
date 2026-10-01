using System.Text.Json;
using Parley.Ipc;
using Umbra.Parley;
using Xunit;

namespace Parley.Tests;

public class WidgetPresentationTests
{
    private static readonly WidgetOptions Defaults = new();

    private static StatusSnapshot Status(int tells = 0, int linkshells = 0, int crossWorld = 0, params StatusConversation[] conversations) => new()
    {
        Version = ParleyIpc.Version,
        LoggedIn = true,
        Tells = tells,
        Linkshells = linkshells,
        CrossWorld = crossWorld,
        Conversations = [.. conversations],
    };

    private static StatusConversation Tell(string name, int unread, string preview = "hello") =>
        new() { Group = 0, Title = name, World = "Gilgamesh", Unread = unread, Sender = name, Preview = preview };

    private static StatusConversation Shell(int group, string name, int unread, string sender, string preview) =>
        new() { Group = group, Title = name, Unread = unread, Sender = sender, Preview = preview };

    private static string Text(StatusSnapshot? status, WidgetOptions? options = null) =>
        WidgetPresentation.Create(status, options ?? Defaults, "Parley is not running.").Text;

    // ---- Nothing to show ----

    [Fact]
    public void Without_parley_the_button_says_why_on_hover()
    {
        var display = WidgetPresentation.Create(null, Defaults, "Parley is not running.");

        Assert.Equal("Chat", display.Text);
        Assert.StartsWith("Parley is not running.", display.Tooltip);
        Assert.Equal(0, display.Unread);
        Assert.True(display.Visible);
    }

    [Fact]
    public void At_the_title_screen_it_asks_you_to_log_in()
    {
        var display = WidgetPresentation.Create(new StatusSnapshot { LoggedIn = false, Tells = 4 }, Defaults, "unused");

        Assert.Equal("Chat", display.Text);
        Assert.Contains("Log in", display.Tooltip);
        Assert.Equal(0, display.Unread);
    }

    [Fact]
    public void With_nothing_unread_it_shows_the_idle_text()
    {
        Assert.Equal("Chat", Text(Status()));
        Assert.Equal("Messages", Text(Status(), Defaults with { IdleText = "Messages" }));
        Assert.Equal("Chat", Text(Status(), Defaults with { IdleText = "" }));
    }

    [Fact]
    public void It_can_hide_itself_until_there_is_something_to_read()
    {
        var hiding = Defaults with { HideWhenIdle = true };

        Assert.False(WidgetPresentation.Create(Status(), hiding, "").Visible);
        Assert.False(WidgetPresentation.Create(null, hiding, "").Visible);
        Assert.True(WidgetPresentation.Create(Status(tells: 1), hiding, "").Visible);
    }

    // ---- Phone style ----

    [Theory]
    [InlineData(1, 0, 0, "1 new Tell")]
    [InlineData(3, 0, 0, "3 new Tells")]
    [InlineData(0, 1, 0, "1 new LS message")]
    [InlineData(0, 5, 0, "5 new LS messages")]
    [InlineData(0, 0, 1, "1 new CWLS message")]
    [InlineData(0, 0, 9, "9 new CWLS messages")]
    [InlineData(2, 5, 1, "2 Tells, 5 LS, 1 CWLS")]
    [InlineData(1, 1, 0, "1 Tell, 1 LS")]
    [InlineData(0, 4, 2, "4 LS, 2 CWLS")]
    public void Phone_style_reads_like_a_notification(int tells, int linkshells, int crossWorld, string expected)
    {
        var display = WidgetPresentation.Create(Status(tells, linkshells, crossWorld), Defaults, "");

        Assert.Equal(expected, display.Text);
        Assert.Equal(tells + linkshells + crossWorld, display.Unread);
    }

    [Fact]
    public void It_can_name_the_sender_when_every_unread_tell_is_from_one_person()
    {
        var naming = Defaults with { NameSingleSender = true };

        Assert.Equal("Tell from Alice Smith", Text(Status(1, 0, 0, Tell("Alice Smith", 1)), naming));
        Assert.Equal("3 Tells from Alice Smith", Text(Status(3, 0, 0, Tell("Alice Smith", 3)), naming));
        Assert.Equal("4 new Tells", Text(Status(4, 0, 0, Tell("Alice Smith", 3), Tell("Bob Jones", 1)), naming));
        // Off by default.
        Assert.Equal("1 new Tell", Text(Status(1, 0, 0, Tell("Alice Smith", 1))));
    }

    [Fact]
    public void It_does_not_name_a_sender_when_the_list_cannot_account_for_every_tell()
    {
        var naming = Defaults with { NameSingleSender = true };

        // Five unread but the one listed conversation holds only two of them.
        Assert.Equal("5 new Tells", Text(Status(5, 0, 0, Tell("Alice Smith", 2)), naming));
    }

    // ---- Filtering ----

    [Fact]
    public void Kinds_that_are_switched_off_do_not_count()
    {
        var tellsOnly = Defaults with { CountLinkshells = false, CountCrossWorld = false };

        var busy = WidgetPresentation.Create(Status(2, 40, 7), tellsOnly, "");
        var quiet = WidgetPresentation.Create(Status(0, 40, 7), tellsOnly, "");

        Assert.Equal("2 new Tells", busy.Text);
        Assert.Equal(2, busy.Unread);
        Assert.Equal("Chat", quiet.Text);
        Assert.Equal(0, quiet.Unread);
    }

    // ---- Other styles ----

    [Fact]
    public void Compact_style_is_a_row_of_counters_that_keeps_its_shape_at_zero()
    {
        var compact = Defaults with { Style = WidgetOptions.StyleCompact };

        Assert.Equal("T:2 FC:0 LS:5 CW:1", Text(Status(2, 5, 1), compact));
        Assert.Equal("T:0 FC:0 LS:0 CW:0", Text(Status(), compact));
        Assert.Equal("T:2 LS:5", Text(Status(2, 5, 1), compact with { CountCrossWorld = false, CountFreeCompany = false }));
        Assert.Equal("T:0 FC:3 LS:0 CW:0", Text(Company(Status(), 3), compact));
    }

    // ---- Free company ----

    private static StatusSnapshot Company(StatusSnapshot status, int unread)
    {
        status.FreeCompany = unread;
        return status;
    }

    [Fact]
    public void Free_company_messages_are_counted_like_the_rest()
    {
        Assert.Equal("1 new FC message", Text(Company(Status(), 1)));
        Assert.Equal("4 new FC messages", Text(Company(Status(), 4)));
        Assert.Equal("2 Tells, 3 FC, 5 LS", Text(Company(Status(2, 5), 3)));
        Assert.Equal("Chat", Text(Company(Status(), 4), Defaults with { CountFreeCompany = false }));

        var display = WidgetPresentation.Create(Company(Status(1), 2), Defaults with { Style = WidgetOptions.StyleTotal }, "");
        Assert.Equal("3 new messages", display.Text);
        Assert.Equal(3, display.Unread);
    }

    [Fact]
    public void The_tooltip_lists_the_free_company_with_who_spoke()
    {
        var status = Company(Status(conversations: Shell(3, "Moonlit Anglers FC", 2, "Isolde Varn", "window is up")), 2);

        var tooltip = WidgetPresentation.Create(status, Defaults, "").Tooltip;

        Assert.Contains("FC · Moonlit Anglers FC (2)", tooltip);
        Assert.Contains("Isolde Varn: window is up", tooltip);
    }

    [Fact]
    public void Total_style_is_one_number()
    {
        var total = Defaults with { Style = WidgetOptions.StyleTotal };

        Assert.Equal("1 new message", Text(Status(0, 1, 0), total));
        Assert.Equal("8 new messages", Text(Status(2, 5, 1), total));
        Assert.Equal("Chat", Text(Status(), total));
    }

    [Fact]
    public void An_unknown_style_falls_back_to_phone()
    {
        Assert.Equal("1 new Tell", Text(Status(1), Defaults with { Style = "Sideways" }));
    }

    // ---- Tooltip ----

    [Fact]
    public void The_tooltip_lists_who_is_waiting_and_what_they_said()
    {
        var status = Status(2, 3, 0,
            Shell(1, "Hunts", 3, "Bob Jones", "S rank in Elpis"),
            Tell("Alice Smith", 2, "are you around?"));

        var tooltip = WidgetPresentation.Create(status, Defaults, "").Tooltip;

        Assert.Contains("LS · Hunts (3)", tooltip);
        Assert.Contains("Bob Jones: S rank in Elpis", tooltip);
        Assert.Contains("Tell · Alice Smith@Gilgamesh (2)", tooltip);
        Assert.Contains("are you around?", tooltip);
        // In a tell the speaker is the title; repeating it is noise.
        Assert.DoesNotContain("Alice Smith: are you around?", tooltip);
        Assert.Contains("Left-click: open or close the chat window", tooltip);
        Assert.Contains("Right-click: mark everything as read", tooltip);
    }

    [Fact]
    public void Previews_can_be_kept_out_of_the_tooltip()
    {
        var status = Status(2, 0, 0, Tell("Alice Smith", 2, "something private"));

        var tooltip = WidgetPresentation.Create(status, Defaults with { ShowPreviews = false }, "").Tooltip;

        Assert.Contains("Alice Smith@Gilgamesh (2)", tooltip);
        Assert.DoesNotContain("something private", tooltip);
    }

    [Fact]
    public void The_tooltip_leaves_out_kinds_that_do_not_count_and_says_when_there_is_more()
    {
        var status = Status(10, 3, 0,
            Shell(1, "Hunts", 3, "Bob Jones", "ignored"),
            Tell("Alice Smith", 2));

        var tooltip = WidgetPresentation.Create(status, Defaults with { CountLinkshells = false }, "").Tooltip;

        Assert.DoesNotContain("Hunts", tooltip);
        Assert.Contains("… and 8 more", tooltip);
    }

    [Fact]
    public void A_long_preview_is_cut_short()
    {
        var status = Status(1, 0, 0, Tell("Alice Smith", 1, new string('z', 300)));

        var tooltip = WidgetPresentation.Create(status, Defaults, "").Tooltip;

        Assert.DoesNotContain(new string('z', 100), tooltip);
        Assert.Contains("z…", tooltip);
    }

    [Fact]
    public void The_tooltip_describes_the_click_the_widget_is_set_to()
    {
        var tooltip = WidgetPresentation.Create(Status(), Defaults with { ToggleOnClick = false }, "").Tooltip;

        Assert.Contains("Left-click: open the chat window", tooltip);
    }
}

public class IpcContractTests
{
    [Fact]
    public void Status_json_uses_the_documented_property_names()
    {
        var snapshot = new StatusSnapshot
        {
            Version = 1, Revision = 42, LoggedIn = true, WindowOpen = true, Tells = 2, Linkshells = 5, CrossWorld = 1,
            Conversations = [new StatusConversation { Group = 0, Title = "Alice Smith", World = "Gilgamesh", Unread = 2, Sender = "Alice Smith", Preview = "it's me", At = 1700000000000 }],
        };

        var json = JsonSerializer.Serialize(snapshot, ParleyIpcJson.Default.StatusSnapshot);

        foreach (var name in new[] { "version", "revision", "loggedIn", "windowOpen", "tells", "linkshells", "crossWorld", "conversations", "group", "title", "world", "unread", "sender", "preview", "at" })
            Assert.Contains($"\"{name}\":", json);
    }

    [Fact]
    public void Status_round_trips()
    {
        var snapshot = new StatusSnapshot
        {
            Version = 1, Revision = 7, LoggedIn = true, Tells = 3,
            Conversations = [new StatusConversation { Group = 2, Title = "Hunts", Unread = 3, Sender = "Bob Jones", Preview = "こんにちは \"quoted\"", At = 5 }],
        };

        var read = JsonSerializer.Deserialize(JsonSerializer.Serialize(snapshot, ParleyIpcJson.Default.StatusSnapshot), ParleyIpcJson.Default.StatusSnapshot);

        Assert.NotNull(read);
        Assert.Equal(7, read.Revision);
        Assert.Equal(3, read.Tells);
        var conversation = Assert.Single(read.Conversations);
        Assert.Equal(2, conversation.Group);
        Assert.Equal("こんにちは \"quoted\"", conversation.Preview);
    }

    [Fact]
    public void A_status_from_a_newer_parley_with_extra_fields_still_reads()
    {
        const string json = "{\"version\":1,\"tells\":2,\"somethingNew\":{\"a\":1},\"loggedIn\":true}";

        var read = JsonSerializer.Deserialize(json, ParleyIpcJson.Default.StatusSnapshot);

        Assert.NotNull(read);
        Assert.Equal(2, read.Tells);
        Assert.True(read.LoggedIn);
        Assert.Empty(read.Conversations);
    }

    [Fact]
    public void Theme_colours_keep_their_high_bit()
    {
        // Opaque colours are above int.MaxValue; they must not come back negative or clipped.
        var theme = new UmbraTheme { Token = 12, Colours = { ["Window.Background"] = 0xFF212021, ["Window.TextMuted"] = 0xB0C0C0C0 } };

        var read = JsonSerializer.Deserialize(JsonSerializer.Serialize(theme, ParleyIpcJson.Default.UmbraTheme), ParleyIpcJson.Default.UmbraTheme);

        Assert.NotNull(read);
        Assert.Equal(12, read.Token);
        Assert.Equal(0xFF212021u, read.Colours["Window.Background"]);
        Assert.Equal(0xB0C0C0C0u, read.Colours["Window.TextMuted"]);
    }
}
