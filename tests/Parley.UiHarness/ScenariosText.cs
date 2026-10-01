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
        s.Shot("list");
        s.Check(s.Stage.Find("Friend · In a duty: Second Board of the Unbroken") != null, "a friend's tell says they are in a duty, and which");
        s.Check(s.Stage.Find("is in a duty and may not see a tell") != null, "the reply box warns that a friend in a duty may not see it");

        s.Open(s.Tell("Oskar Lindqvist"));
        s.Check(s.Stage.Find("Friend · Offline") != null, "and when they are offline");
        s.Check(s.Stage.Find("is offline, so a tell will not reach them") != null, "the reply box warns before writing to an offline friend");

        s.Open(s.Tell("Yuna Hoshizora"));
        s.Check(s.Stage.Find("Busy on Gilgamesh") != null, "busy, and visiting another world");
        s.Check(s.Stage.Find("is busy and may not see a tell") != null, "with a warning for busy too");

        s.Open(s.Tell("Tobias Greywater"));
        s.Check(s.Stage.Find("Friend ·") == null, "someone not on the friend list gets nothing");

        s.Config.ShowFriendStatus = false;
        s.Open(s.Tell("Mira Thorne"));
        s.Check(s.Stage.Find("Friend ·") == null, "and it can be turned off");
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
}
