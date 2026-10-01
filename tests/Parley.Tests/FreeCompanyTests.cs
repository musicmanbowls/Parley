using Parley.Core;
using Parley.Core.History;
using Parley.Core.Theme;
using Xunit;

namespace Parley.Tests;

public class FreeCompanyTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    [Fact]
    public void A_free_company_is_a_channel_with_one_slot()
    {
        Assert.Equal(1, ChannelGroup.FreeCompany.Slots());
        Assert.Equal(8, ChannelGroup.Linkshell.Slots());
        Assert.Equal(0, ChannelGroup.Tell.Slots());
        Assert.Equal("/freecompany", ChannelGroup.FreeCompany.SlotCommand(1));
        Assert.Equal("Free Company", ChannelGroup.FreeCompany.Label());
    }

    [Fact]
    public void Its_tab_comes_straight_after_tells()
    {
        Assert.Equal([ChannelGroup.Tell, ChannelGroup.FreeCompany, ChannelGroup.Linkshell, ChannelGroup.CrossWorld], ChannelGroups.All);
        Assert.Equal(ChannelGroups.Count, ChannelGroups.All.Length);
    }

    [Fact]
    public void Its_history_has_a_folder_and_key_of_its_own()
    {
        Assert.Equal("f:Moonlit Anglers", ConversationKey.ForLinkshell(ChannelGroup.FreeCompany, "Moonlit Anglers"));
        Assert.NotEqual(ConversationKey.ForLinkshell(ChannelGroup.Linkshell, "Moonlit Anglers"), ConversationKey.ForLinkshell(ChannelGroup.FreeCompany, "Moonlit Anglers"));
        Assert.Equal("freecompany/Moonlit Anglers.jsonl", ConversationKey.FileFor(ChannelGroup.FreeCompany, "Moonlit Anglers", string.Empty));
    }

    [Fact]
    public void Messages_go_out_through_the_free_company_command()
    {
        var store = new ConversationStore(null);
        store.LoadCharacter(1, "Wren Alder", "Jenova", saveHistory: false);
        store.SetSlots(ChannelGroup.FreeCompany, ["Moonlit Anglers"]);
        var company = store.InGroup(ChannelGroup.FreeCompany).Single();

        Assert.True(company.CanSend);
        Assert.Equal("/freecompany ", OutgoingQueue.CommandPrefix(company));
    }

    [Fact]
    public void Joining_leaving_and_changing_free_company_keeps_each_history_apart()
    {
        var store = new ConversationStore(null);
        store.LoadCharacter(1, "Wren Alder", "Jenova", saveHistory: false);

        store.SetSlots(ChannelGroup.FreeCompany, ["Moonlit Anglers"]);
        var first = store.GetLinkshell(ChannelGroup.FreeCompany, "Moonlit Anglers", 1);
        store.Add(first, new ChatMessage { Sender = "Isolde Varn", SenderWorld = 40, Text = "welcome!" });

        store.SetSlots(ChannelGroup.FreeCompany, ["Sunset Cartel"]);
        var second = store.GetLinkshell(ChannelGroup.FreeCompany, "Sunset Cartel", 1);

        Assert.Equal(0, first.Slot);
        Assert.False(first.CanSend);
        Assert.Equal(1, second.Slot);
        Assert.Equal(1, first.Unread);
        Assert.Equal(0, second.Unread);
        Assert.Equal(2, store.InGroup(ChannelGroup.FreeCompany).Count);
        Assert.Same(second, store.InGroup(ChannelGroup.FreeCompany)[0]);
    }

    [Fact]
    public void A_message_before_the_name_is_known_joins_the_free_company_when_it_is()
    {
        var store = new ConversationStore(null);
        store.LoadCharacter(1, "Wren Alder", "Jenova", saveHistory: false);

        var waiting = store.GetLinkshell(ChannelGroup.FreeCompany, string.Empty, 1);
        store.Add(waiting, new ChatMessage { Sender = "Isolde Varn", SenderWorld = 40, Text = "morning" });
        Assert.True(waiting.Placeholder);

        store.SetSlots(ChannelGroup.FreeCompany, ["Moonlit Anglers"]);

        var company = store.InGroup(ChannelGroup.FreeCompany).Single();
        Assert.Equal("Moonlit Anglers", company.Title);
        Assert.Equal("morning", company.Messages.Single().Text);
        Assert.Equal(1, company.Unread);
    }

    [Fact]
    public void Unread_free_company_messages_are_counted_on_their_own()
    {
        var store = new ConversationStore(null);
        store.LoadCharacter(1, "Wren Alder", "Jenova", saveHistory: false);
        store.SetSlots(ChannelGroup.FreeCompany, ["Moonlit Anglers"]);
        var company = store.GetLinkshell(ChannelGroup.FreeCompany, "Moonlit Anglers", 1);

        store.Add(company, new ChatMessage { Sender = "Isolde Varn", SenderWorld = 40, Text = "one" });
        store.Add(company, new ChatMessage { Sender = "Pell Marrow", SenderWorld = 40, Text = "two" });

        Assert.Equal(2, store.Unread(ChannelGroup.FreeCompany));
        Assert.Equal(0, store.Unread(ChannelGroup.Linkshell));
        Assert.Equal(2, store.TotalUnread);

        var status = store.BuildStatus(windowOpen: false);
        Assert.Equal(2, status.FreeCompany);
        Assert.Equal(3, status.Conversations.Single().Group);
    }

    [Fact]
    public async Task Its_history_is_found_again_without_an_index()
    {
        using var history = new HistoryStore(temp.Path);
        history.Append("0000000000000001/freecompany/Moonlit Anglers.jsonl", new ChatMessage { Timestamp = 5, Sender = "Isolde Varn", Text = "hi" });

        var scanned = new TaskCompletionSource<List<DiscoveredFile>>();
        history.Scan("0000000000000001", scanned.SetResult);
        var files = await scanned.Task;

        var found = Assert.Single(files);
        Assert.Equal(ChannelGroup.FreeCompany, found.Group);
        Assert.Equal("Moonlit Anglers", found.Name);
        Assert.Equal("freecompany/Moonlit Anglers.jsonl", found.File);
    }

    [Fact]
    public void Summaries_mention_the_free_company_after_tells()
    {
        Assert.Equal("1 Tell, 3 FC, 2 LS", UnreadSummary.Describe(1, 2, 0, 3));
        Assert.Equal("4 FC", UnreadSummary.Describe(0, 0, 0, 4));
    }

    [Fact]
    public void Every_theme_has_a_free_company_colour()
    {
        foreach (var theme in new[] { GameThemes.Dark, GameThemes.Light, GameThemes.ClassicFf, GameThemes.ClearBlue })
            Assert.Equal(GameThemes.DefaultFreeCompany, GameThemes.For(theme).Group(ChannelGroup.FreeCompany));

        Assert.Equal(PaletteSlot.FreeCompany, Palette.GroupSlot(ChannelGroup.FreeCompany));
        Assert.Equal("Free Company", Palette.Label(PaletteSlot.FreeCompany));
    }
}
