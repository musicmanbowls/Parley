using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core;
using Parley.Core.Settings;
using Parley.Core.Theme;
using Parley.Game;

namespace Parley.UiHarness;

/// <summary>Links, selecting and copying, symbols, search, pop-out windows, friends and the game's themes.</summary>
internal static partial class Scenarios
{
    private static void Links(Scenario s)
    {
        GameLinks.Calls.Clear();
        GameLinks.NativeTooltips = false;
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;

        var mira = s.Tell("Mira Thorne");
        Demo.Theirs(s.Store, mira, s.Stage.Clock - 40_000, "Mira Thorne", Demo.Jenova, "the guide is at https://example.com/raid-guide for later");
        Demo.Theirs(s.Store, mira, s.Stage.Clock - 30_000, "Mira Thorne", Demo.Jenova, "video: youtu.be/abc123");
        Demo.TheirsWithItem(s.Store, mira, s.Stage.Clock - 20_000, "Mira Thorne", Demo.Jenova, "could you make me a ", 5059, "Rose Gold Ingot", " please?", rarityColour: 553);
        s.Open(mira);
        s.Shot("links");

        var url = s.Stage.Need("https://example.com/raid-guide");
        s.Check(s.Stage.Find("youtu.be/abc123") != null, "an address without http:// is found as well");

        s.Stage.MoveTo(url.Centre);
        s.Stage.Frames(2);
        s.Check(s.Stage.Find("Click to open in your browser") != null && s.Stage.Find("example.com") != null, "hovering a web link says where it goes");
        s.Shot("url-hover");

        s.Stage.Click(url.Centre);
        s.Check(s.Plugin.OpenedUrls.Contains("https://example.com/raid-guide"), "clicking a web link opens it");
        s.Stage.Click(s.Stage.Need("youtu.be/abc123").Centre);
        s.Check(s.Plugin.OpenedUrls.Contains("https://youtu.be/abc123"), "and one written without http:// opens as https");

        s.Stage.Click(url.Centre, button: 1);
        s.Check(s.Stage.Find("Copy link") != null, "right-clicking a web link offers to copy it");
        s.Stage.ClickText("Copy link");
        s.Check(Stage.Clipboard == "https://example.com/raid-guide", "and copies it");

        // An item someone linked.
        var item = s.Stage.Need("Rose Gold Ingot");
        s.Stage.MoveTo(item.Centre);
        s.Stage.Frames(2);
        s.Check(s.Stage.Find("An ingot of gold") != null, "hovering an item shows what it is");
        s.Shot("item-tooltip");

        s.Stage.Click(item.Centre);
        s.Check(s.Stage.Find("Find in your inventory") != null, "clicking an item opens its menu");
        s.Check(s.Stage.Find("Try on") == null, "with nothing to try on for something that cannot be worn");
        s.Shot("item-menu");
        s.Stage.ClickText("Recipes that use this");
        s.Check(GameLinks.Calls.Contains("recipes 5059"), "the menu's actions go to the game");

        s.Stage.Click(item.Centre);
        s.Stage.ClickText("Market prices on Universalis");
        s.Check(s.Plugin.OpenedUrls.Contains("https://universalis.app/market/5059"), "prices can be looked up on Universalis");

        s.Stage.Click(item.Centre);
        s.Stage.ClickText("Link in your reply");
        s.Stage.Frames(3);
        s.Check(mira.Draft.Contains("Rose Gold Ingot"), "an item can be linked into the reply");

        // The game's own tooltip, where there is one.
        GameLinks.NativeTooltips = true;
        GameLinks.Calls.Clear();
        s.Stage.MoveTo(item.Centre);
        s.Stage.Frames(2);
        s.Check(GameLinks.Calls.Contains("tooltip 5059"), "with the game's tooltips on, hovering an item shows the game's own");
        s.Stage.MoveTo(new Vector2(item.Centre.X, item.Centre.Y + 300f));
        s.Stage.Frames(2);
        s.Check(GameLinks.Calls.Contains("tooltip hidden"), "and moving off the item puts it away");
        GameLinks.NativeTooltips = false;
    }

    private static void SelectAndCopy(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Open(s.Tell("Mira Thorne"));

        var run = s.Stage.Need("we need one more for the alliance raid");
        var from = run.Text.IndexOf("one", StringComparison.Ordinal);
        var to = run.Text.IndexOf("more", StringComparison.Ordinal) + 4;
        s.Stage.Drag(new Vector2(run.Edges[from] + 1f, run.Centre.Y), new Vector2(run.Edges[to], run.Centre.Y));
        s.Shot("selected");
        s.Stage.Chord(ImGuiKey.C, ctrl: true);
        s.Check(Stage.Clipboard == "one more", "dragging across text and pressing Ctrl+C copies exactly that");

        var word = run.Text.IndexOf("alliance", StringComparison.Ordinal);
        s.Stage.DoubleClick(new Vector2(run.Edges[word] + 4f, run.Centre.Y));
        s.Stage.Chord(ImGuiKey.C, ctrl: true);
        s.Check(Stage.Clipboard == "alliance", "double-clicking selects a word");

        var first = s.Stage.Need("hey, are you around tonight?");
        var last = s.Stage.Need("we need one more for the alliance raid");
        s.Stage.Drag(new Vector2(first.Edges[0] + 1f, first.Centre.Y), new Vector2(last.Max.X + 4f, last.Centre.Y));
        s.Shot("across-messages");
        s.Stage.Chord(ImGuiKey.C, ctrl: true);
        s.Check(Stage.Clipboard == "hey, are you around tonight?\nyep, on after the reset. what's up?\nwe need one more for the alliance raid",
            "a selection across messages copies each, one to a line");

        Stage.Clipboard = string.Empty;
        s.Stage.Click(new Vector2(run.Edges[from] + 1f, run.Centre.Y), button: 1);
        s.Check(s.Stage.Find("Copy selected text") != null, "the right-click menu offers to copy the selection");
        s.Stage.ClickText("Copy selected text");
        s.Check(Stage.Clipboard.StartsWith("hey, are you around", StringComparison.Ordinal), "and does");

        s.Stage.Click(new Vector2(run.Edges[from] + 1f, run.Centre.Y));
        Stage.Clipboard = "untouched";
        s.Stage.Chord(ImGuiKey.C, ctrl: true);
        s.Check(Stage.Clipboard == "untouched", "a click on its own clears the selection");

        // Ctrl+C in the reply box is the reply box's own.
        s.Stage.ClickText("Message Mira");
        s.Stage.Frames(3);
        s.Stage.Type("draft text");
        s.Stage.Chord(ImGuiKey.A, ctrl: true);
        s.Stage.Chord(ImGuiKey.C, ctrl: true);
        s.Check(Stage.Clipboard == "draft text", "Ctrl+C while typing copies from the reply box");
    }

    private static void SymbolPicker(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        var mira = s.Tell("Mira Thorne");
        s.Open(mira);

        s.Stage.Type("thanks ");
        s.Stage.ClickIcon(FontAwesomeIcon.Heart);
        s.Shot("picker");
        s.Check(s.Stage.FindExact("★") != null && s.Stage.FindExact("♪") != null, "the symbol button shows symbols the game can draw");

        s.Stage.Click(s.Stage.NeedExact("♥").Centre);
        s.Stage.Frames(3);
        s.Check(mira.Draft == "thanks ♥", "picking one puts it where the caret was");
        s.Stage.Type(" see you");
        s.Check(mira.Draft == "thanks ♥ see you", "and typing carries on after it");
        s.Check(s.Config.RecentSymbols.FirstOrDefault() == "♥", "it is remembered as recently used");

        s.Stage.ClickIcon(FontAwesomeIcon.Heart);
        s.Check(s.Stage.Find("Recent") != null, "recently used symbols come first");
        s.Stage.ClickText("Numbers");
        s.Check(s.Stage.FindExact("①") != null, "circled numbers are there");
        s.Stage.ClickText("Game icons");
        s.Shot("game-icons");
        s.Check(s.Stage.Find("none of these") != null, "symbols the font cannot draw are left out (the stage has no game font)");
    }

    private static void Search(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Open(s.Tell("Mira Thorne"));

        s.Stage.Chord(ImGuiKey.F, ctrl: true);
        s.Stage.Type("prism");
        s.Stage.Frames(30);
        s.Shot("results");
        s.Check(s.Stage.Find("1 match") != null, "Ctrl+F searches the tab's conversations");
        s.Check(s.Stage.Find("Yuna Hoshizora") != null && s.Stage.Find("prism") != null, "the result says where and shows the words around the match");

        s.Stage.ClickText("Yuna Hoshizora");
        s.Stage.Frames(4);
        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, s.Tell("Yuna Hoshizora")), "picking a result opens its conversation");
        s.Check(s.Stage.Find("spare glamour prism") != null, "at the message");
        s.Shot("opened");

        // Everything: across the kinds of conversation.
        s.Stage.ClickIcon(FontAwesomeIcon.Search);
        s.Stage.Frames(2);
        s.Stage.Chord(ImGuiKey.A, ctrl: true);
        s.Stage.Type("timber");
        s.Stage.Frames(30);
        s.Check(s.Stage.Find("Nothing found") != null, "a search in the tells tab does not look in the free company");
        s.Stage.ClickText("All Tells");
        s.Stage.ClickText("Everything");
        s.Stage.Frames(30);
        s.Check(s.Stage.Find("1 match") != null && s.Stage.Find("Lanternlight Society") != null, "searching everything does");
        s.Stage.ClickText("Lanternlight Society");
        s.Stage.Frames(4);
        s.Check(s.Plugin.MainWindow.Selected?.Group == ChannelGroup.FreeCompany, "and its result opens the right tab");
    }

    private static void PopOut(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Open(s.Tell("Mira Thorne"));

        s.Stage.ClickText("Free Company", button: 1);
        s.Stage.ClickText("Pop Free Company out");
        s.Stage.Frames(4);
        var window = s.Plugin.PopOuts[(int)ChannelGroup.FreeCompany];
        s.Check(s.Config.IsPoppedOut(ChannelGroup.FreeCompany) && window.IsOpen, "a tab can be popped out into a window of its own");
        s.Check(s.Stage.Find("FC map run at 9") != null, "which shows that kind of conversation");
        s.Shot("popped");

        window.IsOpen = false;
        s.Stage.Frames(2);
        s.Check(s.Config.IsPoppedOut(ChannelGroup.FreeCompany), "closing the window keeps it popped out");
        s.Stage.ClickText("Free Company");
        s.Stage.Frames(3);
        s.Check(window.IsOpen && ReferenceEquals(s.Plugin.MainWindow.Selected, s.Tell("Mira Thorne")), "its tab in the main window brings it back, and the main window stays where it was");

        s.Stage.Click(s.Stage.NeedIcon(FontAwesomeIcon.CompressAlt).Centre);
        s.Stage.Frames(3);
        s.Check(!s.Config.IsPoppedOut(ChannelGroup.FreeCompany) && !window.IsOpen, "it can be put back");
        s.Check(s.Plugin.MainWindow.Selected?.Group == ChannelGroup.FreeCompany, "and the main window shows it");
    }

    private static void Friends(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Plugin.Friends[("Mira Thorne", Demo.Jenova)] = new FriendStatus(true, true, false, false, true, Demo.Jenova, "Jenova") { Place = "Second Board of the Unbroken" };
        s.Plugin.Friends[("Oskar Lindqvist", Demo.Adamantoise)] = new FriendStatus(true, false, false, false, false, 0, string.Empty);
        s.Plugin.Friends[("Yuna Hoshizora", Demo.Jenova)] = new FriendStatus(true, true, true, false, false, Demo.Gilgamesh, "Gilgamesh");

        s.Open(s.Tell("Mira Thorne"));
        s.Check(s.Stage.Find("Friend ·") == null, "where they are only shows when asked for, to leave the messages the room");
        PointAtTab(s, "Mira Thorne");
        s.Shot("banner");
        s.Check(s.Stage.Find("Friend · In a duty: Second Board of the Unbroken") != null, "pointing at a friend's tab says they are in a duty, and which");
        s.Check(s.Stage.Find("is in a duty and may not see a tell") != null, "the reply box warns that a friend in a duty may not see it");

        s.Open(s.Tell("Oskar Lindqvist"));
        PointAtTab(s, "Oskar Lindqvist");
        s.Check(s.Stage.Find("Friend · Offline") != null, "and when they are offline");
        s.Check(s.Stage.Find("is offline, so a tell will not reach them") != null, "the reply box warns before writing to an offline friend");

        s.Open(s.Tell("Yuna Hoshizora"));
        PointAtTab(s, "Yuna Hoshizora");
        s.Check(s.Stage.Find("Busy on Gilgamesh") != null, "busy, and visiting another world");
        s.Check(s.Stage.Find("is busy and may not see a tell") != null, "with a warning for busy too");

        PointAtTab(s, "Tobias Greywater");
        s.Check(s.Stage.Find("Friend ·") == null, "someone not on the friend list gets nothing");

        s.Config.MainWindowTabs = TabDirection.Vertical;
        s.Open(s.Tell("Mira Thorne"));
        PointAtTab(s, "Mira Thorne");
        s.Shot("list-banner");
        s.Check(s.Stage.Find("Friend · In a duty") != null, "the list down the side shows the same when a row is pointed at");

        s.Config.ShowFriendStatus = false;
        PointAtTab(s, "Mira Thorne");
        s.Check(s.Stage.Find("Friend ·") == null, "and it can be turned off");
    }

    /// <summary>Rests the cursor on a conversation's tab, or its row in the list: the first place its name is drawn.</summary>
    private static void PointAtTab(Scenario s, string name)
    {
        s.Stage.MoveTo(s.Stage.Need(name).Centre);
        s.Stage.Frames(2);
    }

    private static void GameThemeLooks(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Config.Theme = ThemeMode.Game;
        s.Open(s.Tell("Mira Thorne"));

        foreach (var theme in new[] { GameThemes.ClearWhite, GameThemes.ClearGreen, GameThemes.ClearGrey, GameThemes.ClearPink })
        {
            s.Stage.Plugin.GameColours.Theme = theme;
            s.Stage.Frames(3);
            s.Check(s.Plugin.Theme.Palette.WindowBg == GameThemes.For(theme).WindowBg, $"the game set to {GameThemes.Name(theme)} is followed");
            s.Shot(GameThemes.Name(theme).Replace(' ', '-').ToLowerInvariant());
        }

        s.Config.GameThemeOverride = GameThemes.ClassicFf;
        s.Stage.Frames(3);
        s.Check(s.Plugin.Theme.Palette.WindowBg == GameThemes.For(GameThemes.ClassicFf).WindowBg, "a theme picked for Parley wins over the game's");
        s.Config.GameThemeOverride = -1;
    }

    /// <summary>A filter table that leaves out <paramref name="hide"/>, or with <paramref name="showOnly"/> everything else.</summary>
    private static byte[] Filter(int[]? hide = null, int[]? showOnly = null)
    {
        var table = new byte[4096];
        if (showOnly != null)
        {
            Array.Fill(table, (byte)0xFF);
            foreach (var kind in showOnly) table[ChatLogFilter.Pack(kind) >> 3] &= (byte)~(1 << (ChatLogFilter.Pack(kind) & 7));
        }
        foreach (var kind in hide ?? []) table[ChatLogFilter.Pack(kind) >> 3] |= (byte)(1 << (ChatLogFilter.Pack(kind) & 7));
        return table;
    }

    private static void GameLine(Scenario s, int kind, string text) =>
        s.Plugin.General.Add(new ChatMessage { Timestamp = s.Plugin.Store.Now, Text = text, LogInfo = ChatLogFilter.Pack(kind) });

    private static void GeneralChat(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Config.ReleaseKeyboardOnEnter = false;

        // The game's tabs as a player might have them: Event takes NPC dialogue, Battle the echoes.
        s.Plugin.General.SetTabs(
        [
            new GameChatTab(0, "General", true, Filter(hide: [61])),
            new GameChatTab(1, "Battle", true, Filter(showOnly: [56])),
            new GameChatTab(2, "Event", true, Filter(showOnly: [61])),
            new GameChatTab(3, string.Empty, false, Filter(showOnly: [])),
        ]);
        GameLine(s, 10, "Bren Halloway: anyone up for a hunt train?");
        GameLine(s, 14, "(Tobias Greywater) pulling in five");
        GameLine(s, 24, "[FC]<Hana Birchwood> house party tonight!");
        GameLine(s, 16, "[1]<Pell Marrow> selling cordials");
        GameLine(s, 13, "Yuna Hoshizora >> are you around?");
        GameLine(s, 57, "You have 3 unread letters.");
        GameLine(s, 61, "Tataru: Welcome back!");
        GameLine(s, 56, "check, check");

        s.Open(s.Tell("Mira Thorne"));
        s.Check(s.Stage.Find("General") == null, "no General tab until it is turned on");

        s.Config.GeneralChat = true;
        s.Stage.Frames(2);
        s.Stage.ClickText("General");
        s.Stage.Frames(3);
        s.Shot("general");
        s.Check(s.Stage.Find("anyone up for a hunt train?") != null, "the game's chat shows in General");
        s.Check(s.Stage.Find("house party tonight!") != null, "free company lines too");
        s.Check(s.Stage.Find("Welcome back!") == null, "a line the game's General tab leaves out is left out here as well");
        s.Check(s.Stage.Find("Battle") != null && s.Stage.Find("Event") != null, "the game's tabs in use are there by name");
        s.Check(s.Stage.Find("Tab 4") == null, "and a tab the game does not use is not");

        s.Stage.ClickText("Event");
        s.Stage.Frames(2);
        s.Check(s.Stage.Find("Welcome back!") != null, "NPC dialogue shows in the tab the game shows it in");
        s.Check(s.Stage.Find("anyone up for a hunt train?") == null, "and nothing that tab leaves out");
        s.Shot("event-tab");

        s.Stage.ClickText("General", occurrence: 1);
        s.Stage.Frames(2);
        s.Check(s.Stage.Find("Party") != null, "the box shows the channel the game's chat box is on");
        s.Stage.ClickText("Say something in Party");
        s.Stage.Type("on my way");
        s.Stage.Press(ImGuiKey.Enter);
        s.Check(s.Plugin.SentGeneral.LastOrDefault() == "on my way", "Enter types the line into the game's chat box");

        s.Stage.Type("/em waves");
        s.Stage.Press(ImGuiKey.Enter);
        s.Check(s.Plugin.SentGeneral.LastOrDefault() == "/em waves", "commands go to the game as typed");

        // Up and Down go back through what was sent, as in the game's chat box.
        s.Stage.Type("half typed");
        s.Stage.Press(ImGuiKey.UpArrow);
        s.Check(s.Stage.Find("/em waves") != null && s.Stage.Find("half typed") == null, "Up brings back the last line sent");
        s.Stage.Press(ImGuiKey.UpArrow);
        s.Check(s.Stage.Find("on my way") != null, "and Up again the one before");
        s.Stage.Press(ImGuiKey.DownArrow);
        s.Stage.Press(ImGuiKey.DownArrow);
        s.Check(s.Stage.Find("half typed") != null, "Down past the newest gives back what was being typed");
        for (var i = 0; i < 10; i++) s.Stage.Press(ImGuiKey.Backspace);

        s.Stage.ClickText("Party");
        s.Stage.Frames(2);
        s.Shot("channels");
        s.Stage.ClickText("Free Company", occurrence: 1);
        s.Check(s.Plugin.SentGeneral.LastOrDefault() == "/fc", "picking a channel switches the game's chat box to it");

        s.Stage.ClickText("Tells");
        s.Stage.Frames(2);
        s.Check(s.Stage.Find("anyone up for a hunt train?") == null, "a conversation tab leaves General");
        s.Config.GeneralChat = false;
    }

    private static void WindowLook(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Config.GeneralChat = true;
        s.Plugin.General.SetTabs([new GameChatTab(0, "General", true, Filter())]);
        GameLine(s, 10, "Bren Halloway: anyone up for a hunt train?");
        GameLine(s, 14, "(Tobias Greywater) pulling in five");
        GameLine(s, 24, "[FC]<Hana Birchwood> house party tonight!");

        s.Open(s.Tell("Mira Thorne"));
        s.Stage.ClickText("General");
        s.Config.SoftEdges = true;
        s.Config.ShowTitleBar = false;
        s.Stage.MoveTo(new Vector2(s.Stage.Width - 4f, s.Stage.Height - 4f));
        s.Stage.Frames(3);
        s.Shot("soft-edges");
        s.Check(s.Stage.Find("anyone up for a hunt train?") != null, "the window still shows its lines with soft edges and no title bar");

        // Left alone, it fades back; pointing at it brings it straight back.
        s.Config.FadeWhenIdle = true;
        s.Config.FadeAfterSeconds = 2;
        s.Config.IdleOpacity = 0f;
        s.Config.IdleTextOpacity = 0.4f;
        ImGuiP.FocusWindow(default);
        s.Stage.Frames(2);
        System.Threading.Thread.Sleep(2200);
        s.Stage.Frames(90);
        s.Shot("faded");

        s.Stage.MoveTo(s.Stage.Need("anyone up for a hunt train?").Centre);
        s.Stage.Frames(30);
        s.Shot("woken");

        s.Config.FadeWhenIdle = false;
        s.Config.SoftEdges = false;
        s.Config.ShowTitleBar = true;
        s.Config.GeneralChat = false;
    }

    private static void ChatHides(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Config.GeneralChat = true;
        s.Plugin.General.SetTabs([new GameChatTab(0, "General", true, Filter())]);
        GameLine(s, 10, "Bren Halloway: anyone up for a hunt train?");
        s.Open(s.Tell("Mira Thorne"));
        s.Stage.ClickText("General");
        s.Stage.Frames(2);

        // Standing in for the game's chat, Parley goes when that would, as in a cutscene.
        s.Plugin.ReplacingGameChat = true;
        s.Plugin.ChatHidden = true;
        s.Stage.Frames(2);
        s.Check(s.Stage.Find("anyone up for a hunt train?") == null, "standing in for the game's chat, Parley is out of sight when that would be");

        // Enter, or Send Tell on a player, calls it up to type in.
        s.Plugin.MainWindow.TypeInGeneral(prefill: "/tell Mira Thorne@Jenova ");
        s.Stage.Frames(3);
        s.Shot("called-up");
        s.Check(s.Stage.Find("anyone up for a hunt train?") != null, "Enter calls it up to type in");
        s.Check(s.Stage.Find("/tell Mira Thorne@Jenova") != null, "and Send Tell starts the tell in its box");

        ImGuiP.FocusWindow(default);
        System.Threading.Thread.Sleep(600);
        s.Stage.Frames(3);
        s.Check(s.Stage.Find("anyone up for a hunt train?") == null, "once the keyboard goes elsewhere it is out of sight again");

        s.Plugin.ChatHidden = false;
        s.Stage.Frames(2);
        s.Check(s.Stage.Find("anyone up for a hunt train?") != null, "and back as soon as the game's chat would be");

        s.Plugin.ReplacingGameChat = false;
        s.Config.GeneralChat = false;
    }

    private static void SectionLooks(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;

        // Each section its own: tells as a compact log with no times, the free company in bubbles.
        s.Config.SameLookEverywhere = false;
        var tells = s.Config.LookFor(LookSection.Tells);
        tells.Layout = MessageStyle.Log;
        tells.Timestamps = TimestampStyle.None;
        tells.Scale = 0.8f;
        s.Config.LookFor(LookSection.FreeCompany).Layout = MessageStyle.Bubbles;

        s.Open(s.Tell("Mira Thorne"));
        s.Stage.Frames(3);
        s.Shot("tells-log");
        s.Check(s.Stage.Find("hey, are you around tonight?") != null, "a section with its own look still shows its messages");

        s.Plugin.MainWindow.Show(s.Plugin.Store.Find(ConversationKey.ForLinkshell(ChannelGroup.FreeCompany, "Lanternlight Society")));
        s.Stage.Frames(3);
        s.Shot("free-company-bubbles");

        // A narrow window keeps room for the conversation: a list down the side shows icons.
        tells.MainWindowTabs = TabDirection.Vertical;
        s.Open(s.Tell("Mira Thorne"));
        s.Stage.Frames(2);
        s.Check(s.Stage.Find("Oskar") != null, "the list has names in a wide window");
        var window = s.Plugin.MainWindow;
        window.Size = new Vector2(440, 480);
        window.SizeCondition = ImGuiCond.Always;
        s.Stage.Frames(3);
        s.Shot("narrow");
        s.Check(s.Stage.Find("Oskar") == null, "and only icons once the window is narrow");
        s.Check(s.Stage.Find("hey, are you around tonight?") != null, "leaving the room to the conversation");

        window.Size = new Vector2(780, 520);
        s.Stage.Frames(2);
        window.SizeCondition = ImGuiCond.FirstUseEver;
        s.Config.SameLookEverywhere = true;
    }

    private static void AutoTranslatePicker(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        var mira = s.Tell("Mira Thorne");
        s.Open(mira);

        // As in the game's chat box: the start of a phrase, then Tab.
        s.Stage.ClickText("Message Mira Thorne");
        s.Stage.Type("thanks, Than");
        s.Stage.Press(ImGuiKey.Tab);
        s.Stage.Frames(2);
        s.Shot("tab");
        s.Check(s.Stage.Find("Thank you.") != null, "Tab opens the auto-translate picker on the word typed");
        s.Stage.ClickText("Thank you.");
        s.Stage.Frames(2);
        s.Check(mira.Draft == "thanks, Thank you. ", "picking a phrase puts it in place of that word");

        // Or the button beside the box, a group, and a phrase.
        s.Stage.ClickIcon(FontAwesomeIcon.Language);
        s.Stage.Frames(2);
        s.Stage.ClickText("Tactics");
        s.Stage.Frames(2);
        s.Shot("groups");
        s.Stage.ClickText("Stack up!");
        s.Stage.Frames(2);
        s.Check(mira.Draft.Contains("Stack up!", StringComparison.Ordinal), "the button's picker adds a phrase where the caret was");

        s.Stage.ClickIcon(FontAwesomeIcon.Language);
        s.Stage.Frames(2);
        s.Stage.Type("midgard");
        s.Stage.Frames(2);
        s.Check(s.Stage.Find("Midgardsormr") != null && s.Stage.Find("Mounts") != null, "searching finds phrases in every group, with the group's name");
        s.Stage.Press(ImGuiKey.Escape);
        s.Stage.Frames(2);
        s.Plugin.Store.SetDraft(mira, string.Empty);
    }
}
