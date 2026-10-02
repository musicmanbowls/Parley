using Parley.Core;
using Xunit;

namespace Parley.Tests;

public class GeneralLogTests
{
    private const int Say = 10;
    private const int Echo = 56;
    private const int NpcDialogue = 61;

    private static byte[] Hiding(params int[] kinds)
    {
        var table = new byte[4096];
        foreach (var kind in kinds)
        {
            var info = ChatLogFilter.Pack(kind);
            table[info >> 3] |= (byte)(1 << (info & 7));
        }
        return table;
    }

    private static ChatMessage Line(int kind, string text) => new() { Text = text, LogInfo = ChatLogFilter.Pack(kind) };

    private static GeneralLog WithTabs(byte[] general, byte[] events)
    {
        var log = new GeneralLog();
        log.SetTabs([new GameChatTab(0, "General", true, general), new GameChatTab(1, "Event", true, events)]);
        return log;
    }

    [Fact]
    public void EachTabGetsTheLinesItsFilterShows()
    {
        var log = WithTabs(Hiding(NpcDialogue), Hiding(Say, Echo));
        var hello = Line(Say, "hello");
        var npc = Line(NpcDialogue, "Welcome back!");
        log.Add(hello);
        log.Add(npc);

        Assert.Equal([hello], log.Shown(0));
        Assert.Equal([npc], log.Shown(1));
        Assert.Equal([hello, npc], log.All);
    }

    [Fact]
    public void ChangingAFilterInTheGameSortsEveryLineAgain()
    {
        var log = WithTabs(Hiding(NpcDialogue), Hiding(Say));
        var npc = Line(NpcDialogue, "Welcome back!");
        log.Add(npc);
        Assert.Empty(log.Shown(0));

        log.SetTabs([new GameChatTab(0, "General", true, Hiding()), new GameChatTab(1, "Event", true, Hiding(Say))]);

        Assert.Equal([npc], log.Shown(0));
    }

    [Fact]
    public void LinesBeforeTheTabsAreReadAreSortedOnceTheyAre()
    {
        var log = new GeneralLog();
        var hello = Line(Say, "hello");
        log.Add(hello);
        Assert.Empty(log.Shown(0));

        log.SetTabs([new GameChatTab(0, "General", true, Hiding())]);

        Assert.Equal([hello], log.Shown(0));
    }

    [Fact]
    public void TheOldestLinesGoOnceThereAreTooMany()
    {
        var log = WithTabs(Hiding(), Hiding(Say));
        var first = Line(Say, "first");
        log.Add(first);
        for (var i = 0; i < GeneralLog.Capacity; i++) log.Add(Line(Say, $"line {i}"));

        Assert.Equal(GeneralLog.Capacity, log.All.Count);
        Assert.DoesNotContain(first, log.All);
        Assert.DoesNotContain(first, log.Shown(0));
        Assert.Equal(GeneralLog.Capacity, log.Shown(0).Count);
    }

    [Fact]
    public void ReadingTheSameTabsAgainChangesNothing()
    {
        var log = WithTabs(Hiding(NpcDialogue), Hiding(Say));
        log.Add(Line(Say, "hello"));
        var revision = log.Revision;

        log.SetTabs([new GameChatTab(0, "General", true, Hiding(NpcDialogue)), new GameChatTab(1, "Event", true, Hiding(Say))]);

        Assert.Equal(revision, log.Revision);
    }

    [Fact]
    public void RenamingATabIsNoticed()
    {
        var log = WithTabs(Hiding(), Hiding());
        var revision = log.Revision;

        log.SetTabs([new GameChatTab(0, "Main", true, Hiding()), new GameChatTab(1, "Event", true, Hiding())]);

        Assert.NotEqual(revision, log.Revision);
        Assert.Equal("Main", log.Tabs[0].Name);
    }

    [Fact]
    public void ClearingLetsGoOfEverything()
    {
        var log = WithTabs(Hiding(), Hiding());
        log.Add(Line(Say, "hello"));

        log.Clear();

        Assert.Empty(log.All);
        Assert.Empty(log.Shown(0));
    }

    [Fact]
    public void ChannelsAreNumberedAsTheGameNumbersThem()
    {
        Assert.Equal("Party", ChatChannels.ById(2)!.Name);
        Assert.Equal("/p", ChatChannels.ById(2)!.Command);
        Assert.Null(ChatChannels.ById(ChatChannels.TellId));

        var ls3 = ChatChannels.ById(21)!;
        Assert.Equal((ChannelGroup.Linkshell, 3, 18), (ls3.Group, ls3.Slot, ls3.LogKind));

        Assert.Equal(37, ChatChannels.ById(9)!.LogKind);
        Assert.Equal(101, ChatChannels.ById(10)!.LogKind);
        Assert.Equal("/cwl8", ChatChannels.ById(16)!.Command);
    }
}
