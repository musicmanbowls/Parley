using System.Numerics;
using Dalamud.Interface;
using Parley.Core;
using Parley.Core.Settings;

namespace Parley.UiHarness;

internal static partial class Scenarios
{
    /// <summary>
    /// Tabs across the top, the main window's way of picking a conversation:
    /// more than fit scroll sideways, they can be cut down to pictures, and
    /// pointing at one shows who it is. Popped out, a section has the list
    /// down the side, and the main window can have it too.
    /// </summary>
    private static void ConversationTabs(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;

        // More people than fit across the window, all older than the demo's own.
        string[] names = ["Ashe Tallow", "Brannoc Vale", "Cyra Moss", "Dellan Pike", "Esk Rowan", "Faye Lindell", "Garret Holm"];
        for (var i = 0; i < names.Length; i++)
        {
            var tell = s.Store.OpenTell(names[i], Demo.Faerie, "Faerie");
            Demo.Theirs(s.Store, tell, s.Stage.Clock - ((10 + i) * 3_600_000L), names[i], Demo.Faerie, $"hello from {names[i]}");
        }

        s.Open(s.Tell("Mira Thorne"));
        s.Shot("overflowing");
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.AngleDoubleLeft) == null, "the main window has tabs across the top, not a list");
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.AngleRight) != null, "with more tabs than fit, arrows reach the rest");
        s.Check(s.Stage.Find("Garret Holm") == null, "the furthest tab starts out of sight");

        s.Stage.MoveTo(s.Stage.Need("Mira Thorne").Centre);
        s.Stage.Wheel(-10f);
        s.Stage.Frames(2);
        s.Shot("scrolled");
        s.Check(s.Stage.Find("Garret Holm") != null, "the wheel over the tabs brings the rest into view");

        s.Stage.ClickText("Garret Holm");
        s.Check(ReferenceEquals(s.Plugin.MainWindow.Selected, s.Tell("Garret Holm")), "clicking a tab opens that conversation");
        s.Check(s.Stage.Find("hello from Garret Holm") != null, "with its messages");

        s.Plugin.MainWindow.Show(s.Tell("Mira Thorne"));
        s.Stage.Frames(2);
        s.Check(s.Stage.Find("Mira Thorne") != null, "a conversation picked elsewhere has its tab scrolled into view");

        s.Stage.ClickIcon(FontAwesomeIcon.UserCircle);
        s.Stage.Frames(2);
        s.Shot("pictures");
        s.Check(s.Config.TabIconsOnly, "the button at the end of the tabs cuts them to pictures");
        s.Check(s.Stage.FindExact("Yuna Hoshizora") == null, "with no names on them");
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.AngleRight) == null, "so they all fit without scrolling");

        s.Stage.MoveTo(s.Stage.NeedExact("MT").Centre);
        s.Stage.Frames(2);
        s.Shot("pointed-at");
        s.Check(s.Stage.Find("Mira Thorne") != null && s.Stage.Find("Jenova") != null, "pointing at a picture shows who it is and their world");

        s.Stage.ClickIcon(FontAwesomeIcon.Font);
        s.Check(!s.Config.TabIconsOnly, "and the names come back");

        // Popped out, the list down the side is the default.
        s.Plugin.PopOut(ChannelGroup.Tell);
        s.Stage.Frames(4);
        s.Shot("popped-out");
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.AngleDoubleLeft) != null || s.Stage.FindIcon(FontAwesomeIcon.AngleDoubleRight) != null,
            "a section in a window of its own has the list down the side");
        s.Plugin.DockBack(ChannelGroup.Tell);
        s.Stage.Frames(3);

        // And the main window can have the list too.
        s.Config.MainWindowTabs = TabDirection.Vertical;
        s.Stage.Frames(3);
        s.Shot("main-window-list");
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.AngleDoubleLeft) != null && s.Stage.FindIcon(FontAwesomeIcon.UserCircle) == null,
            "the main window can have the list down the side instead");

        // Each section can have its own.
        s.Config.SameLookEverywhere = false;
        s.Config.LookFor(LookSection.Linkshells).MainWindowTabs = TabDirection.Horizontal;
        s.Stage.ClickText("Linkshells");
        s.Stage.Frames(2);
        s.Check(s.Stage.FindIcon(FontAwesomeIcon.UserCircle) != null && s.Stage.FindIcon(FontAwesomeIcon.AngleDoubleLeft) == null,
            "with a look for each section, linkshells keep tabs while tells have the list");
    }

    /// <summary>General's "+", which has the game add a chat tab, and the cog for the game's own log settings.</summary>
    private static void GeneralTabButtons(Scenario s)
    {
        Demo.Seed(s.Stage);
        s.Config.JumpToUnreadOnOpen = false;
        s.Config.GeneralChat = true;
        s.Plugin.General.SetTabs(
        [
            new GameChatTab(0, "General", true, Filter(hide: [61])),
            new GameChatTab(1, "Battle", true, Filter(showOnly: [56])),
            new GameChatTab(2, string.Empty, false, Filter(showOnly: [])),
            new GameChatTab(3, string.Empty, false, Filter(showOnly: [])),
        ]);

        s.Open(s.Tell("Mira Thorne"));
        s.Stage.ClickText("General");
        s.Stage.Frames(3);
        s.Shot("buttons");

        // The window's own "+" (a new tell) and cog (Parley's settings) are higher up, in the strip of sections.
        var add = Lowest(s, FontAwesomeIcon.Plus);
        var cog = Lowest(s, FontAwesomeIcon.Cog);
        s.Check(add != null && cog != null && add.Value.Y > s.Stage.Need("Battle").Min.Y - 4f, "General has a + and a cog beside the game's tabs");

        s.Stage.Click(add!.Value);
        s.Check(s.Plugin.TabsAsked == 1, "+ has the game add a tab");

        // The game asks for a name, then the tab is there.
        s.Plugin.General.SetTabs(
        [
            new GameChatTab(0, "General", true, Filter(hide: [61])),
            new GameChatTab(1, "Battle", true, Filter(showOnly: [56])),
            new GameChatTab(2, "Trade", true, Filter(showOnly: [10])),
            new GameChatTab(3, string.Empty, false, Filter(showOnly: [])),
        ]);
        s.Stage.Frames(3);
        s.Shot("added");
        s.Check(s.Stage.Find("Trade") != null, "the new tab shows up in General");
        s.Check(s.Config.GeneralTab == 2, "and General goes to it");

        s.Stage.Click(Lowest(s, FontAwesomeIcon.Cog)!.Value);
        s.Check(s.Plugin.LogSettingsOpened == 1 && s.Plugin.SettingsOpened == 0, "the cog opens the game's log settings, not Parley's");

        s.Plugin.General.SetTabs(
        [
            new GameChatTab(0, "General", true, Filter(hide: [61])),
            new GameChatTab(1, "Battle", true, Filter(showOnly: [56])),
            new GameChatTab(2, "Trade", true, Filter(showOnly: [10])),
            new GameChatTab(3, "Linkshells", true, Filter(showOnly: [16])),
        ]);
        s.Stage.Frames(3);
        s.Shot("four-tabs");
        s.Check(Lowest(s, FontAwesomeIcon.Plus) is not { } plus || plus.Y < s.Stage.Need("Battle").Min.Y, "with all four of the game's tabs in use there is no +");

        s.Plugin.General.SetTabs(
        [
            new GameChatTab(0, "General", true, Filter(hide: [61])),
            new GameChatTab(1, "Battle", true, Filter(showOnly: [56])),
            new GameChatTab(2, string.Empty, false, Filter(showOnly: [])),
            new GameChatTab(3, string.Empty, false, Filter(showOnly: [])),
        ]);
        s.Plugin.GameChatReachable = false;
        s.Stage.Frames(3);
        s.Stage.Click(Lowest(s, FontAwesomeIcon.Plus)!.Value);
        s.Stage.Frames(2);
        s.Shot("unreachable");
        s.Check(s.Stage.Find("could not be reached") != null, "when the game's chat log cannot be reached, General says so");
    }

    /// <summary>
    /// Where the icon of this kind furthest down the screen is. Icons drawn
    /// close together read back as one run, so each is looked for inside runs.
    /// </summary>
    private static Vector2? Lowest(Scenario s, FontAwesomeIcon icon)
    {
        var glyph = (char)icon;
        Vector2? lowest = null;
        foreach (var run in s.Stage.Text)
        {
            if (!run.Icon || !run.Visible) continue;
            for (var i = 0; i < run.Text.Length; i++)
            {
                if (run.Text[i] != glyph) continue;
                var centre = new Vector2((run.Edges[i] + run.Edges[i + 1]) * 0.5f, run.Centre.Y);
                if (lowest == null || centre.Y > lowest.Value.Y) lowest = centre;
            }
        }
        return lowest;
    }
}
