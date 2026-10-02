using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core;
using Parley.Core.History;
using Parley.Core.Settings;

namespace Parley.UiHarness;

/// <summary>One run of the window under test: a stage, and a list of what was expected of it.</summary>
internal sealed class Scenario(string name, Stage stage, HistoryStore? history)
{
    public string Name { get; } = name;
    public Stage Stage { get; } = stage;
    public HistoryStore? History { get; } = history;
    public List<string> Failures { get; } = [];
    public int Checks { get; private set; }

    public Plugin Plugin => Stage.Plugin;
    public ConversationStore Store => Stage.Plugin.Store;
    public Configuration Config => Stage.Plugin.Config;

    public void Check(bool condition, string what)
    {
        Checks++;
        if (!condition) Failures.Add(what);
    }

    public void Shot(string suffix = "") => Stage.Shot(suffix.Length > 0 ? $"{Name}-{suffix}" : Name);

    /// <summary>Opens the window on a conversation, the way clicking its row would, and lets it settle.</summary>
    public void Open(Conversation? conversation = null)
    {
        Plugin.MainWindow.Show(conversation);
        Stage.Frames(4);
    }

    /// <summary>Lets the history worker finish what it has been asked, then lets the store hear about it.</summary>
    public void Settle()
    {
        // One job on the history worker can queue another, so ask more than once.
        for (var i = 0; i < 4; i++)
        {
            History?.Flush();
            Stage.Frames(2);
        }
    }

    public Conversation Tell(string name) =>
        Store.FindTellByName(name) ?? throw new InvalidOperationException($"No tell with {name}.");

    public Conversation Shell(ChannelGroup group, string name) =>
        Store.Find(ConversationKey.ForLinkshell(group, name)) ?? throw new InvalidOperationException($"No linkshell called {name}.");

    /// <summary>Prints everything on screen, for working out why something was not found.</summary>
    public void Dump()
    {
        foreach (var run in Stage.Text) Console.WriteLine($"        {(run.Icon ? "icon " : string.Empty)}{(run.Visible ? string.Empty : "(clipped) ")}{run}");
    }
}

internal static partial class Scenarios
{
    private sealed record Definition(string Name, Action<Scenario> Body, float Scale = 1f, bool History = false);

    private static readonly Definition[] All =
    [
        new("01-opens-on-unread", OpensOnUnread),
        new("02-tells", Tells),
        new("03-cross-world", CrossWorld),
        new("04-log-layout", LogLayout),
        new("05-time-on-every-message", TimeOnEveryMessage),
        new("06-sidebar", Sidebar),
        new("07-scrolling", Scrolling),
        new("08-themes", Themes),
        new("09-new-tell", NewTell),
        new("10-menus", Menus),
        new("11-sending", Sending),
        new("12-settings", Settings),
        new("13-scaled", Scaled, Scale: 1.5f),
        new("14-empty", Empty),
        new("15-history", HistoryPaging, History: true),
        new("16-quiet-open", QuietOpen),
        new("17-free-company", FreeCompany),
        new("18-alt-r", AltR),
        new("19-name-colours", NameColours),
        new("20-alerts", Alerts),
        new("21-links", Links),
        new("22-select-and-copy", SelectAndCopy),
        new("23-symbols", SymbolPicker),
        new("24-search", Search),
        new("25-pop-out", PopOut),
        new("26-friends", Friends),
        new("27-game-themes", GameThemeLooks),
        new("28-general", GeneralChat),
        new("29-window-look", WindowLook),
        new("30-chat-hides", ChatHides),
        new("31-section-looks", SectionLooks),
        new("32-auto-translate", AutoTranslatePicker),
        new("33-conversation-tabs", ConversationTabs),
        new("34-general-tab-buttons", GeneralTabButtons),
    ];

    public static int RunAll(string output, string assets, string? filter)
    {
        var failed = 0;
        var checks = 0;
        var ran = 0;
        foreach (var definition in All)
        {
            if (filter != null && !definition.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            ran++;

            string? temporary = null;
            HistoryStore? history = null;
            if (definition.History)
            {
                temporary = Path.Combine(Path.GetTempPath(), "parley-harness-" + Guid.NewGuid().ToString("N"));
                history = new HistoryStore(temporary, (what, ex) => Console.WriteLine($"        history: could not {what}: {ex.Message}"));
            }

            using var stage = new Stage(output, assets, definition.Scale, history);
            var scenario = new Scenario(definition.Name, stage, history);
            try
            {
                definition.Body(scenario);
            }
            catch (Exception ex)
            {
                scenario.Failures.Add($"threw: {ex.Message}");
                try
                {
                    scenario.Shot("at-failure");
                    scenario.Dump();
                }
                catch (Exception) { /* nothing drawn yet */ }
            }
            finally
            {
                history?.Dispose();
                if (temporary != null && Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            }

            scenario.Failures.AddRange(stage.Problems);
            checks += scenario.Checks;

            if (scenario.Failures.Count == 0)
            {
                Console.WriteLine($"ok    {definition.Name} ({scenario.Checks} checks, {stage.FrameCount} frames)");
                continue;
            }

            failed++;
            Console.WriteLine($"FAIL  {definition.Name}");
            foreach (var failure in scenario.Failures) Console.WriteLine($"        {failure}");
        }

        Console.WriteLine();
        if (ran == 0) Console.WriteLine("No scenario matches that filter.");
        else if (failed == 0) Console.WriteLine($"All {ran} scenarios passed ({checks} checks). Screenshots are in {output}");
        else Console.WriteLine($"{failed} of {ran} scenarios failed.");
        return failed == 0 && ran > 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // Looks
    // ------------------------------------------------------------------

    private static void OpensOnUnread(Scenario s)
    {
        Demo.Seed(s.Stage);
        var hunts = s.Shell(ChannelGroup.Linkshell, "Hunt Train NA");
        s.Check(hunts.Unread == 3, "the hunt linkshell starts with three unread messages");
        s.Check(s.Store.TotalUnread == 7, "seven messages are unread in all");

        s.Open();
        s.Shot();

        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, hunts), "the window opens on the conversation with the newest unread message");
        s.Check(s.Stage.FindExact("New") != null, "the first unread message is marked");
        s.Check(s.Stage.Find("pulling the first mark now") != null, "the unread messages are on screen");
        s.Check(hunts.Unread == 0, "a conversation on screen in a focused window is read");
        s.Check(s.Store.TotalUnread == 4, "the other unread conversations are still unread");
        s.Check(s.Stage.Find("Parley (4)") != null, "the title bar counts what is unread");
    }

    private static void Tells(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Open();
        s.Check(s.Plugin.MainWindow.Selected is { IsTell: true }, "with nothing else asked for, the window opens on the last tab used");

        s.Stage.ClickText("Mira Thorne");
        s.Shot();

        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, s.Tell("Mira Thorne")), "clicking a row selects that conversation");
        s.Check(s.Stage.Find("on my way") != null, "the newest message is on screen straight away");
        s.Check(s.Stage.Find("Jenova") != null, "the header says which world they are on");

        var yuna = s.Tell("Yuna Hoshizora");
        Demo.Theirs(s.Store, yuna, s.Stage.Clock, "Yuna Hoshizora", Demo.Jenova, "got it, thank you so much!");
        s.Stage.Frames(2);
        s.Shot("unread");
        s.Check(yuna.Unread == 1, "a tell arriving in a conversation that is not on screen is unread");
        s.Check(s.Store.Unread(ChannelGroup.Tell) == 1, "and is the only unread tell");

        s.Stage.ClickText("Yuna Hosh");
        s.Check(yuna.Unread == 0, "selecting the conversation reads it");
        s.Check(s.Stage.FindExact("New") != null, "with the unread message marked");
        s.Shot("read");
    }

    private static void CrossWorld(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Open();
        s.Stage.ClickText("Cross-world");
        s.Stage.ClickText("Aether Hunts");
        s.Shot();

        s.Check(s.Plugin.MainWindow.Selected?.Group == ChannelGroup.CrossWorld, "the cross-world tab shows cross-world linkshells");
        s.Check(s.Stage.Find("Gilgamesh") != null, "people in a cross-world linkshell are shown with their world");
        s.Check(s.Config.LastGroup == ChannelGroup.CrossWorld, "the tab is remembered");
    }

    private static void LogLayout(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.MessageStyle = MessageStyle.Log;
        s.Open(s.Shell(ChannelGroup.Linkshell, "Moonlit Anglers"));
        s.Shot();

        s.Check(s.Stage.Find("You") != null, "in the flat layout the local player's messages are labelled");
        s.Check(s.Stage.Find("Isolde Varn") != null, "and so are everyone else's");
    }

    private static void TimeOnEveryMessage(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.Timestamps = TimestampStyle.EveryMessage;
        s.Open(s.Tell("Mira Thorne"));
        s.Shot("bubbles");
        s.Check(s.Stage.Find("19:35") != null, "each bubble carries its time");

        s.Config.MessageStyle = MessageStyle.Log;
        s.Config.Use24Hour = false;
        s.Stage.Frames(2);
        s.Shot("log-12h");
        s.Check(s.Stage.Find("7:35 PM") != null, "the twelve-hour clock is used when asked for");
    }

    private static void Sidebar(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.MainWindowTabs = TabDirection.Vertical;
        s.Config.JumpToUnreadOnOpen = false;
        s.Open(s.Tell("Mira Thorne"));
        s.Store.SetPinned(s.Tell("Yuna Hoshizora"), true);
        s.Store.SetMuted(s.Tell("Oskar Lindqvist"), true);
        s.Store.SetDraft(s.Tell("Yuna Hoshizora"), "sending it now, check your");
        s.Stage.Frames(2);
        s.Shot("pinned-muted-draft");
        s.Check(s.Stage.Find("Draft:") != null, "a draft shows in place of the last message");

        s.Config.SidebarPreviews = false;
        s.Stage.Frames(2);
        s.Shot("one-line");

        s.Config.SidebarPreviews = true;
        s.Stage.ClickIcon(FontAwesomeIcon.AngleDoubleLeft);
        s.Shot("collapsed");
        s.Check(s.Config.SidebarCollapsed, "the arrow at the foot of the list shrinks it");
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.AngleDoubleRight) != null, "and turns round to bring it back");

        s.Stage.ClickIcon(FontAwesomeIcon.AngleDoubleRight);
        s.Check(!s.Config.SidebarCollapsed, "clicking it again brings the list back");
    }

    private static void Themes(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Open(s.Tell("Mira Thorne"));

        s.Config.Theme = ThemeMode.Game;
        string[] names = ["dark", "light", "classic", "clear-blue"];
        for (var theme = 0; theme < names.Length; theme++)
        {
            s.Plugin.GameColours.Theme = theme;
            s.Stage.Frames(2);
            s.Shot("game-" + names[theme]);
        }

        s.Plugin.GameColours.Theme = 0;
        s.Stage.ClickText("Linkshells");
        s.Stage.ClickText("Moonlit Anglers");
        s.Shot("game-dark-linkshell");

        s.Config.Theme = ThemeMode.Umbra;
        s.Stage.Frames(2);
        s.Check(!s.Plugin.Theme.Themed, "the Umbra theme falls back to Dalamud's style until Umbra has sent its colours");

        // Umbra's own default profile, as its RegisterDefaultColors sets it (0xAABBGGRR).
        s.Plugin.Theme.SetUmbra(new Dictionary<string, uint>
        {
            ["Window.Background"] = 0xFF212021, ["Window.BackgroundLight"] = 0xFF292829, ["Window.Border"] = 0xFF484848,
            ["Window.TitlebarBackground"] = 0xFF101010, ["Window.TitlebarGradient1"] = 0xFF2F2E2F, ["Window.ScrollbarThumb"] = 0xFF484848,
            ["Window.Text"] = 0xFFD0D0D0, ["Window.TextMuted"] = 0xB0C0C0C0, ["Window.AccentColor"] = 0xFF4C8EB9,
            ["Input.Background"] = 0xFF151515, ["Widget.Background"] = 0xFF101010, ["Widget.BackgroundHover"] = 0xFF2F2F2F,
        }, token: 7);
        s.Stage.Frames(2);
        s.Shot("umbra");
        s.Check(s.Plugin.Theme.Themed, "and follows them once it has");
    }

    private static void Scaled(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Open(s.Tell("Mira Thorne"));
        s.Shot();
        s.Check(s.Stage.Find("hey, are you around tonight?") != null, "the conversation is laid out at a larger UI scale");
    }

    private static void Empty(Scenario s)
    {
        s.Open();
        s.Shot("logged-out");
        s.Check(s.Stage.Find("Log in") != null, "logged out, the window says so");

        s.Store.LoadCharacter(0x0040000012345678, s.Plugin.LocalName, "Jenova", saveHistory: false);
        s.Stage.Frames(3);
        s.Shot("no-conversations");
        s.Check(s.Stage.Find("No tells yet") != null, "with no conversations, the window explains how to start one");

        s.Stage.ClickText("Linkshells");
        s.Shot("no-linkshells");
        s.Check(s.Stage.Find("not in any linkshells") != null, "and says when there are no linkshells");
    }

    // ------------------------------------------------------------------
    // Behaviour
    // ------------------------------------------------------------------

    private static void Scrolling(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        var mira = s.Tell("Mira Thorne");
        Demo.Pad(s.Stage, mira, 120, "Mira Thorne", Demo.Jenova);
        s.Open(mira);
        s.Shot("at-the-end");

        s.Check(s.Stage.FindExact("reply number 120") != null, "a long conversation opens at its newest message");
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.ArrowDown) == null, "with no button to jump to the end, being there already");

        var list = s.Stage.NeedExact("reply number 120").Centre;
        s.Stage.MoveTo(list);
        s.Stage.Wheel(3);
        s.Shot("scrolled-up");
        s.Check(s.Stage.FindExact("reply number 120") == null, "the wheel scrolls towards older messages");
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.ArrowDown) != null, "and the way back to the end appears");

        // Something to watch: whichever message is nearest the middle of the view.
        var anchor = s.Stage.Text.Where(run => run.Visible && run.Text.StartsWith("line number", StringComparison.Ordinal))
            .OrderBy(run => MathF.Abs(run.Centre.Y - list.Y + 150f)).First();
        var anchorText = anchor.Text;
        var before = anchor.Min.Y;

        Demo.Theirs(s.Store, mira, s.Stage.Clock, "Mira Thorne", Demo.Jenova, "a message that arrives while reading back");
        Demo.Theirs(s.Store, mira, s.Stage.Clock, "Mira Thorne", Demo.Jenova, "and another one");
        s.Stage.Frame();
        s.Check(s.Stage.FindExact(anchorText)?.Min.Y == before, "messages arriving below do not move what is being read, not even for a frame");
        s.Stage.Frames(2);
        s.Shot("new-below");
        s.Check(s.Stage.FindExact(anchorText)?.Min.Y == before, "and it stays put afterwards");
        s.Check(InList(s, "and another one") == null, "the new messages are out of sight below");

        s.Stage.ClickIcon(FontAwesomeIcon.ArrowDown);
        s.Shot("back-at-the-end");
        s.Check(InList(s, "and another one") != null, "the button goes to the newest message");
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.ArrowDown) == null, "and then goes away");

        Demo.Theirs(s.Store, mira, s.Stage.Clock, "Mira Thorne", Demo.Jenova, "one more, while following");
        s.Stage.Frame();
        s.Check(s.Stage.FindExact("one more, while following") != null, "at the end, a new message is on screen the frame it arrives");

        // The scrollbar: drag the thumb to the top of its track.
        var bar = ScrollbarX(s);
        s.Stage.Drag(new Vector2(bar, list.Y - 20f), new Vector2(bar, 0f));
        s.Shot("dragged-to-top");
        s.Check(InList(s, "hey, are you around tonight?") != null, "dragging the scrollbar to the top shows the first message");

        // Switching conversation and back starts at the end again, on the first frame.
        s.Plugin.MainWindow.Show(s.Tell("Yuna Hoshizora"));
        s.Stage.Frame();
        s.Check(s.Stage.FindExact("I do! I'll mail it over") != null, "another conversation is at its newest message on the first frame it is shown");
        s.Plugin.MainWindow.Show(mira);
        s.Stage.Frame();
        s.Check(s.Stage.FindExact("one more, while following") != null, "and coming back is too");
    }

    /// <summary>The x of the middle of the message list's scrollbar, which ends level with the send button.</summary>
    private static float ScrollbarX(Scenario s)
    {
        var send = s.Stage.NeedIcon(FontAwesomeIcon.PaperPlane);
        return send.Max.X - 12f;
    }

    /// <summary>A piece of text in the message list. With tabs across the top there are no previews, so that is the only place it can be.</summary>
    private static TextRun? InList(Scenario s, string text) => s.Stage.FindExact(text);

    private static void NewTell(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Open();

        s.Stage.ClickIcon(FontAwesomeIcon.Plus);
        s.Shot("suggestions");
        s.Check(s.Stage.Find("Send a tell to") != null, "the plus button opens the new tell box");
        s.Check(s.Stage.Find("friend, online") != null, "friends are suggested");

        s.Stage.Type("osk");
        s.Shot("filtered");
        s.Check(s.Stage.Find("Oskar Lindqvist@Adamantoise") != null, "typing narrows the suggestions");
        s.Check(s.Stage.Find("Yuna Hoshizora@Jenova") == null, "to the ones that match");

        s.Stage.Press(ImGuiKey.Enter);
        s.Check(s.Stage.Find("Enter a character name") != null || s.Stage.Find("Add the world") != null, "a name that is not one is refused, and says why");
        s.Shot("refused");

        // Replace what was typed with a full name and world.
        for (var i = 0; i < 3; i++) s.Stage.Press(ImGuiKey.Backspace);
        s.Stage.Type("Rook Ashdown@Faerie");
        s.Stage.Press(ImGuiKey.Enter);
        s.Shot("opened");

        var rook = s.Store.FindTellByName("Rook Ashdown");
        s.Check(rook != null, "a full name and world starts a conversation");
        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, rook), "and selects it");
        s.Check(s.Stage.Find("This is the start of your conversation with Rook Ashdown") != null, "an empty conversation says what it is");
    }

    private static void Menus(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Open(s.Shell(ChannelGroup.Linkshell, "Moonlit Anglers"));

        s.Stage.ClickText("Old Friends", button: 1);
        s.Shot("row-menu");
        s.Check(s.Stage.Find("Pin to top") != null, "right-clicking a tab opens its menu");
        s.Check(s.Stage.Find("Delete conversation") != null, "a linkshell the character has left can be deleted");

        s.Stage.ClickText("Pin to top");
        s.Check(s.Shell(ChannelGroup.Linkshell, "Old Friends").Pinned, "the menu pins the conversation");

        s.Stage.ClickText("got it on the second cast", button: 1);
        s.Shot("message-menu");
        s.Check(s.Stage.Find("Copy message") != null, "right-clicking a message opens its menu");
        s.Check(s.Stage.Find("Send a tell to Hana Birchwood") != null, "which offers a tell to whoever said it");

        s.Stage.ClickText("Send a tell to Hana Birchwood");
        s.Check(s.Plugin.MainWindow.Selected is { IsTell: true, Title: "Hana Birchwood" }, "and starts one");
        s.Shot("tell-from-menu");

        s.Open(s.Shell(ChannelGroup.Linkshell, "Moonlit Anglers"));
        s.Stage.ClickText("Moonlit Anglers", button: 1);
        s.Shot("tab-menu");
        s.Stage.ClickText("Clear history");
        s.Shot("confirm");
        s.Check(s.Stage.Find("Clear the history of Moonlit Anglers?") != null, "clearing history asks first");

        s.Stage.ClickText("Cancel");
        s.Check(s.Shell(ChannelGroup.Linkshell, "Moonlit Anglers").Messages.Count > 0, "cancelling keeps the messages");

        s.Stage.ClickText("Moonlit Anglers", button: 1);
        s.Stage.ClickText("Clear history");
        s.Stage.ClickText("Clear", occurrence: 1);
        s.Check(s.Shell(ChannelGroup.Linkshell, "Moonlit Anglers").Messages.Count == 0, "confirming removes them");
        s.Shot("cleared");
    }

    private static void Sending(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        var mira = s.Tell("Mira Thorne");
        s.Open(mira);

        // The reply box has focus as the window opens.
        s.Stage.Type("see you there");
        s.Check(mira.Draft == "see you there", "typing goes into the reply box without clicking it first");
        s.Shot("typed");

        // Alt+Enter is the key that brings Parley up to type in. Already typing, it does nothing.
        s.Stage.Chord(ImGuiKey.Enter, alt: true);
        s.Stage.Frames(3);
        s.Check(s.Plugin.SentLines.Count == 0 && mira.Draft == "see you there", "Alt+Enter while typing does not send");
        s.Stage.Type(" soon");
        s.Check(mira.Draft == "see you there soon", "and the box keeps the keyboard");
        for (var i = 0; i < " soon".Length; i++) s.Stage.Press(ImGuiKey.Backspace);

        s.Stage.Press(ImGuiKey.Enter);
        s.Stage.Frames(30);
        s.Shot("sent");
        s.Check(s.Plugin.SentLines.Count == 1 && s.Plugin.SentLines[0] == "/tell Mira Thorne@Jenova see you there", "Enter sends the message as a tell");
        s.Check(mira.Draft.Length == 0, "and empties the box");
        s.Check(s.Stage.Find("see you there") != null, "the message appears when the game echoes it");

        // Like the game's own chat box, Enter is the end of typing.
        s.Check(!s.Plugin.MainWindow.IsFocused, "Enter hands the keyboard back to the game");
        s.Stage.Type("second");
        s.Check(mira.Draft.Length == 0, "so keys pressed afterwards do not go into the reply box");

        // The plugin sends the next Enter back to the window it came from.
        s.Check(ReferenceEquals(s.Plugin.ChatInUse, s.Plugin.MainWindow), "the window stays the chat in use for the next Enter");
        s.Plugin.MainWindow.StartTyping();
        s.Stage.Frames(3);
        s.Stage.Type("back again");
        s.Check(mira.Draft == "back again", "Enter with Parley in use puts the cursor back in its reply box");
        s.Store.SetDraft(mira, string.Empty);
        s.Stage.Press(ImGuiKey.Enter);
        s.Stage.Frames(2);

        s.Stage.ClickText("Message Mira");
        s.Stage.Frames(3);
        s.Stage.Type("second");
        s.Check(mira.Draft == "second", "clicking the box gives it the keyboard again");
        s.Stage.Press(ImGuiKey.Enter);
        s.Stage.Frames(30);

        // Enter on an empty box lets go too, with nothing sent.
        s.Stage.ClickText("Message Mira");
        s.Stage.Frames(3);
        var before = s.Plugin.SentLines.Count;
        s.Stage.Press(ImGuiKey.Enter);
        s.Check(!s.Plugin.MainWindow.IsFocused && s.Plugin.SentLines.Count == before, "Enter on an empty box lets go of the keyboard and sends nothing");

        // With that turned off, the box keeps the keyboard for the next message.
        s.Config.ReleaseKeyboardOnEnter = false;
        s.Stage.ClickText("Message Mira");
        s.Stage.Frames(3);
        s.Stage.Type("on my way now");
        s.Stage.Press(ImGuiKey.Enter);
        s.Stage.Frames(30);
        s.Stage.Type("second");
        s.Check(mira.Draft == "second", "with that setting off, the box keeps focus after sending");

        // Escape must not throw away what has been typed.
        s.Stage.Press(ImGuiKey.Escape);
        s.Check(mira.Draft == "second", "Escape does not discard a message in progress");

        // A draft waiting in another conversation is added to, not replaced.
        var yuna = s.Tell("Yuna Hoshizora");
        s.Store.SetDraft(yuna, "about the prism: ");
        s.Stage.ClickText("Yuna Hosh");

        // The box takes the keyboard a couple of frames after the click, and a
        // key pressed in the very frame it does is not taken as typing. No
        // hand types that fast; this does, so it waits like one would.
        s.Stage.Frames(3);
        s.Stage.Type("thank you");
        s.Shot("draft-kept");
        s.Check(yuna.Draft == "about the prism: thank you", "selecting a conversation with a draft puts the caret after it");

        // The game refusing a message.
        s.Plugin.RefuseNext = "Unable to send /tell. Yuna Hoshizora is not online.";
        s.Stage.Press(ImGuiKey.Enter);
        s.Stage.Frames(30);
        s.Shot("refused");
        s.Check(s.Stage.Find("Not sent:") != null, "a refusal from the game is shown in the conversation");
        s.Check(yuna.Draft == "about the prism: thank you", "and the text is handed back");

        // A message too long for one line.
        s.Store.SetDraft(yuna, string.Empty);
        s.Stage.Frames(2);
        var text = string.Join(' ', Enumerable.Repeat("a fairly long sentence that goes on for a while", 14));
        s.Stage.Type(text);
        s.Shot("long");
        s.Check(s.Stage.Find("Will be sent as 2 messages") != null, "a long message says how it will be split");

        var sent = s.Plugin.SentLines.Count;
        s.Stage.Press(ImGuiKey.Enter);
        s.Stage.Frames(120);
        s.Check(s.Plugin.SentLines.Count == sent + 2, "and goes out in that many parts");
        s.Shot("split");

        // A linkshell the character has left cannot be written to.
        s.Open(s.Shell(ChannelGroup.Linkshell, "Old Friends"));
        s.Shot("cannot-send");
        s.Check(s.Stage.Find("You are no longer in this linkshell") != null, "a linkshell that has been left says why it cannot be written to");
    }

    private static void Settings(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Plugin.OpenSettings();
        s.Stage.Frames(3);
        s.Shot("general");
        s.Check(s.Stage.Find("Conversations to keep") != null, "the settings window opens on its first tab");

        foreach (var tab in new[] { "Appearance", "Notifications", "History", "Umbra" })
        {
            s.Stage.ClickText(tab);
            s.Shot(tab.ToLowerInvariant());
        }

        s.Stage.ClickText("Appearance");
        s.Stage.ClickText("Layout, tabs and size");
        s.Stage.Frames(2);
        s.Shot("layout-tabs-and-size");
        s.Check(s.Stage.Find("Tabs in the main window") != null && s.Stage.Find("Horizontal: tabs across the top") != null, "the tabs are set under Layout, tabs and size, horizontal in the main window to begin with");
        s.Check(s.Stage.Find("Vertical: a list down the side") != null, "and vertical when popped out");

        s.Stage.ClickText("Appearance");
        s.Config.Theme = ThemeMode.Custom;
        s.Config.CustomColours = Core.Theme.GameThemes.For(Core.Theme.GameThemes.Dark).ToHex();
        s.Plugin.Theme.InvalidateCustom();
        s.Stage.Frames(2);
        s.Shot("custom-theme");
        s.Check(s.Stage.Find("Start from the game's theme") != null, "the custom theme shows its palette editor");
    }

    private static void HistoryPaging(Scenario s)
    {
        var store = s.Store;
        store.PageSize = 40;
        store.LoadCharacter(0x0040000012345678, s.Plugin.LocalName, "Jenova", saveHistory: true);
        s.Settle();

        var mira = store.OpenTell("Mira Thorne", Demo.Jenova, "Jenova");
        Demo.Pad(s.Stage, mira, 100, "Mira Thorne", Demo.Jenova);
        s.Settle();
        store.FlushIndex();
        s.Settle();

        // A new session: everything is on disk and nothing is in memory.
        store.UnloadCharacter();
        store.LoadCharacter(0x0040000012345678, s.Plugin.LocalName, "Jenova", saveHistory: true);
        s.Settle();
        mira = s.Tell("Mira Thorne");
        s.Check(mira.Messages.Count == 0, "after logging in again nothing is loaded until it is looked at");

        s.Open(mira);
        s.Settle();
        s.Shot("first-page");
        s.Check(mira.Messages.Count == 40, "opening a conversation loads the newest page");
        s.Check(s.Stage.Find("reply number 100") != null, "and shows its newest message");

        // Drag the scrollbar to the top of what is loaded: there is a button there for more.
        var list = s.Stage.NeedExact("reply number 100").Centre;
        var bar = ScrollbarX(s);
        s.Stage.Drag(new Vector2(bar, list.Y - 20f), new Vector2(bar, 0f));
        s.Shot("top-of-page");
        s.Check(s.Stage.Find("Load earlier messages") != null, "at the top of what is loaded there is a way to load more");

        // What is on screen must not move when older messages go in above it.
        var anchor = s.Stage.Text.First(run => run.Visible && run.Text.StartsWith("line number", StringComparison.Ordinal));
        var anchorText = anchor.Text;
        var before = anchor.Min.Y;

        s.Stage.ClickText("Load earlier messages");
        s.Settle();
        s.Shot("second-page");
        s.Check(mira.Messages.Count == 80, "the button loads the page before");
        s.Check(s.Stage.FindExact(anchorText)?.Min.Y == before, "without moving what was on screen");

        // Scrolling up past the top with the wheel loads more by itself.
        s.Stage.MoveTo(s.Stage.NeedExact(anchorText).Centre);
        for (var i = 0; i < 40 && !mira.ReachedStart; i++)
        {
            s.Stage.Wheel(3);
            s.Settle();
        }
        s.Check(mira.Messages.Count == 100, "the wheel loads what is left when it reaches the top");
        s.Check(mira.ReachedStart, "and the store knows it has reached the beginning");

        for (var i = 0; i < 60; i++) s.Stage.Wheel(3);
        s.Shot("the-beginning");
        s.Check(s.Stage.Find("line number 1 of") != null, "scrolling right back reaches the first message");
        s.Check(s.Stage.Find("Load earlier messages") == null, "where there is nothing more to load");
    }

    private static void FreeCompany(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Open();

        var company = s.Shell(ChannelGroup.FreeCompany, "Lanternlight Society");
        s.Check(company.Unread == 2, "free company messages count as unread like any other");
        s.Check(s.Stage.Find("Free Company") != null, "there is a Free Company tab");

        s.Stage.ClickText("Free Company");
        s.Shot();
        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, company), "the tab opens on the free company");
        s.Check(company.Unread == 0, "and reads it");
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.UserCircle) == null && s.Stage.FindIcon(FontAwesomeIcon.AngleDoubleLeft) == null, "with only one free company there are no tabs or list to choose from");
        s.Check(s.Stage.Find("FC map run at 9") != null, "its messages are shown");

        s.Stage.Type("I'll bring the maps");
        s.Stage.Press(ImGuiKey.Enter);
        s.Stage.Frames(30);
        s.Check(s.Plugin.SentLines.LastOrDefault() == "/freecompany I'll bring the maps", "a reply goes to free company chat");
        s.Check(s.Stage.Find("I'll bring the maps") != null, "and shows up when the game echoes it");
        s.Shot("replied");

        // Leaving for another free company keeps the old one's history, and the list comes back to choose between them.
        s.Store.SetSlots(ChannelGroup.FreeCompany, ["Sunset Cartel"]);
        var next = s.Store.GetLinkshell(ChannelGroup.FreeCompany, "Sunset Cartel", 1);
        Demo.Theirs(s.Store, next, s.Stage.Clock, "Rook Ashdown", Demo.Faerie, "welcome aboard!");
        s.Stage.Frames(3);
        s.Shot("two-companies");
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.UserCircle) != null, "with an old free company to go back to, there are tabs to choose between them");
        s.Check(s.Stage.Find("Sunset Cartel") != null && s.Stage.Find("Lanternlight Soc") != null, "listing both");
        s.Check(s.Stage.Find("no longer in this free company") != null, "the old one cannot be written to, and says why");
    }

    private static void AltR(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        var tells = s.Store.InGroup(ChannelGroup.Tell).ToArray();
        s.Open(tells[0]);
        s.Check(tells.Length == 4, "there are four tells to step through");

        s.Stage.Chord(ImGuiKey.R, alt: true);
        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, tells[1]), "Alt+R goes to the next conversation in the list");
        s.Shot("next");

        s.Stage.Chord(ImGuiKey.R, alt: true);
        s.Stage.Chord(ImGuiKey.R, alt: true);
        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, tells[3]), "and on down the list");

        s.Stage.Chord(ImGuiKey.R, alt: true);
        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, tells[0]), "from the last it wraps round to the first");

        s.Stage.Chord(ImGuiKey.R, alt: true, shift: true);
        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, tells[3]), "Alt+Shift+R goes back");

        // What is being typed stays with its own conversation.
        s.Stage.Type("half a thought");
        s.Check(tells[3].Draft == "half a thought", "typing after Alt+R goes to the conversation it moved to");
        s.Stage.Chord(ImGuiKey.R, alt: true);
        s.Stage.Frames(3);
        s.Check(tells[3].Draft == "half a thought", "moving on keeps the draft where it was typed");
        s.Check(tells[0].Draft.Length == 0, "and does not carry it into the next conversation");
        s.Stage.Type("hi");
        s.Check(tells[0].Draft == "hi", "the next conversation's own box takes the typing");

        // Within the linkshell tab it steps through linkshells.
        s.Stage.ClickText("Linkshells");
        var shells = s.Store.InGroup(ChannelGroup.Linkshell).ToArray();
        var from = s.Plugin.MainWindow.Selected;
        s.Stage.Chord(ImGuiKey.R, alt: true);
        var at = Array.IndexOf(shells, from);
        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, shells[(at + 1) % shells.Length]), "in the linkshell tab it steps through linkshells");

        // Not while something else has focus.
        var before = s.Plugin.MainWindow.Selected;
        s.Stage.Click(new Vector2(s.Stage.Width - 20f, s.Stage.Height - 20f));
        s.Check(!s.Plugin.MainWindow.IsFocused, "clicking outside the window takes focus away from it");
        s.Stage.Chord(ImGuiKey.R, alt: true);
        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, before), "Alt+R does nothing while the window does not have focus");

        // And not when turned off.
        s.Open(before);
        s.Config.CycleWithAltR = false;
        s.Stage.Chord(ImGuiKey.R, alt: true);
        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, before), "or when it has been turned off");
    }

    private static void NameColours(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Open(s.Shell(ChannelGroup.Linkshell, "Moonlit Anglers"));

        s.Stage.ClickText("got it on the second cast", button: 1);
        s.Check(s.Stage.Find("Name colour for Hana Birchwood") != null, "a message's menu offers a colour for whoever said it");
        s.Stage.ClickText("Name colour for Hana Birchwood");
        s.Shot("picker");
        s.Check(s.Stage.Find("Hana Birchwood@Jenova") != null, "the picker says whose colour it is setting");

        // Pick a colour the way the picker would, by writing it through the same call.
        s.Config.SetNameColour("Hana Birchwood@Jenova", "#40E0D0FF");
        s.Stage.Frames(2);
        s.Shot("coloured");
        s.Stage.ClickText("Done");
        var hana = s.Stage.FindExact("Hana Birchwood");
        s.Check(hana != null && (hana.Colour & 0x00FFFFFF) == 0x00D0E040, "her name is drawn in the colour picked for her");
        s.Check(s.Stage.Find("Automatic") == null, "Done closes the picker");

        // Your own name.
        s.Stage.ClickText("I'll be there, save me a spot on the rocks", button: 1);
        s.Check(s.Stage.Find("Your name colour") != null, "your own messages offer a colour for your name");
        s.Stage.Click(new Vector2(s.Stage.Width - 20f, s.Stage.Height - 20f));

        // A tell's avatar takes the colour too, and the settings list it.
        s.Config.SetNameColour("Mira Thorne@Jenova", "#FF6A00FF");
        s.Plugin.OpenSettings();
        s.Stage.Frames(3);
        s.Stage.ClickText("Appearance");
        s.Stage.Frames(2);
        s.Check(s.Stage.Find("Picked for particular people") == null, "the settings start with their sections closed");
        s.Stage.ClickText("Names");
        s.Stage.Frames(2);
        s.Shot("settings");
        s.Check(s.Stage.Find("Picked for particular people") != null, "the settings list the colours that have been picked");
        s.Check(s.Stage.Find("Mira Thorne@Jenova") != null, "by name");

        s.Stage.ClickText("Automatic", occurrence: 0);
        s.Check(s.Config.NameColours.Count == 1, "and can put someone back to automatic");
    }

    private static void Alerts(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Plugin.OpenSettings();
        s.Stage.Frames(3);
        s.Stage.ClickText("Notifications");
        s.Stage.Frames(2);
        s.Shot("none");
        s.Check(s.Stage.Find("Free Company") != null && s.Stage.Find("Cross-world") != null, "every kind of conversation has its own settings");

        s.Config.Alert(ChannelGroup.Tell).Sound = Core.Settings.AlertSound.Chime;
        s.Config.Alert(ChannelGroup.FreeCompany).Sound = Core.Settings.AlertSound.Game;
        s.Config.Alert(ChannelGroup.FreeCompany).GameSound = 4;
        s.Config.Alert(ChannelGroup.Linkshell).Sound = Core.Settings.AlertSound.File;
        s.Config.Alert(ChannelGroup.Linkshell).Toast = true;
        s.Stage.Frames(2);
        s.Shot("set");
        s.Check(s.Stage.Find("Volume") != null, "Parley's own sounds have a volume");
        s.Check(s.Stage.Find("volume of the game's system sounds") != null, "and the game's sounds say where their volume comes from");
        s.Check(s.Stage.Find("No file chosen yet") != null, "a sound file waits to be chosen");

        s.Stage.ClickText("Test");
        s.Check(s.Plugin.AlertsTested.Contains(ChannelGroup.Tell), "the Test button plays the sound");
    }

    private static void QuietOpen(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Store.MarkAllRead();

        // A tell arrives while the window is closed, and the setting to open it is on.
        var mira = s.Tell("Mira Thorne");
        s.Stage.MoveTo(new Vector2(s.Stage.Width - 5f, s.Stage.Height - 5f));
        Demo.Theirs(s.Store, mira, s.Stage.Clock, "Mira Thorne", Demo.Jenova, "are you still coming?");
        s.Plugin.MainWindow.Show(mira, focus: false);
        s.Stage.Frames(6);
        s.Shot();

        s.Check(s.Plugin.MainWindow.IsOpen, "the window opens");
        s.Check(!s.Plugin.MainWindow.IsFocused, "without taking focus");
        s.Check(mira.Unread == 1, "so the tell still counts as unread");

        s.Stage.MoveTo(s.Stage.Need("are you still coming?").Centre);
        s.Stage.Frames(3);
        s.Check(mira.Unread == 0, "until the cursor is over the window");
    }
}
