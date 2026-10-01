using System.Numerics;
using Parley.Core;
using Parley.Core.History;
using Parley.Core.Settings;
using Parley.Core.Text;
using Xunit;

namespace Parley.Tests;

public class LinkFinderTests
{
    [Theory]
    [InlineData("see https://example.com/page now", "https://example.com/page")]
    [InlineData("HTTP://Example.com", "HTTP://Example.com")]
    [InlineData("go to www.lodestone.com.", "https://www.lodestone.com")]
    [InlineData("watch youtu.be/dQw4w9WgXcQ!", "https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("join discord.gg/abc", "https://discord.gg/abc")]
    [InlineData("(https://en.wikipedia.org/wiki/Mog_(Final_Fantasy))", "https://en.wikipedia.org/wiki/Mog_(Final_Fantasy)")]
    [InlineData("prices at universalis.app/market/5059, cheap", "https://universalis.app/market/5059")]
    public void Finds_addresses(string text, string url)
    {
        var found = LinkFinder.Find(text);
        Assert.Single(found);
        Assert.Equal(url, found[0].Url);
    }

    [Theory]
    [InlineData("e.g. this")]
    [InlineData("version 3.5 is out")]
    [InlineData("open file.txt please")]
    [InlineData("do it.now")]
    [InlineData("mail me at someone@example.com")]
    [InlineData("ftp://example.com/file")]
    [InlineData("just words here")]
    public void Leaves_ordinary_text_alone(string text) => Assert.Empty(LinkFinder.Find(text));

    [Fact]
    public void Positions_point_at_the_address_itself()
    {
        const string text = "the guide (https://example.com) is good";
        var found = Assert.Single(LinkFinder.Find(text));
        Assert.Equal("https://example.com", text.Substring(found.Start, found.Length));
    }
}

public class RichTextTests
{
    [Fact]
    public void Plain_text_has_its_addresses_made_into_links()
    {
        var text = RichText.Plain("look: example.com and more");
        var link = Assert.Single(text.Links);
        Assert.Equal(LinkKind.Url, link.Kind);
        Assert.Equal("example.com", text.Text[link.Start..link.End]);
        Assert.Same(link, text.LinkAt(link.Start + 2));
        Assert.Null(text.LinkAt(0));
        Assert.Equal(text.Text.Length, text.Runs.Sum(run => run.Length));
    }

    [Fact]
    public void Links_from_the_game_keep_their_place_and_colour()
    {
        var builder = new RichText.Builder();
        builder.Append("buy ");
        builder.BeginLink(new TextLink { Kind = LinkKind.Item, Id = 1_005_059 });
        builder.Append("Rose Gold Ingot", 553);
        builder.EndLink();
        builder.Append(" please");
        var text = builder.Build();

        var item = Assert.Single(text.Links);
        Assert.Equal(LinkKind.Item, item.Kind);
        Assert.Equal("Rose Gold Ingot", text.Text[item.Start..item.End]);
        Assert.Equal((ushort)553, text.Runs[text.RunAt(item.Start)].Colour);
        Assert.Equal((ushort)0, text.Runs[text.RunAt(0)].Colour);
    }

    [Fact]
    public void Icons_are_left_out_of_copies()
    {
        var builder = new RichText.Builder();
        builder.Append("on ");
        builder.AppendObject([1, 2, 3]);
        builder.Append("Gilgamesh");
        var text = builder.Build();

        Assert.Equal("on Gilgamesh", text.Copy(0, text.Text.Length));
        Assert.Single(text.Objects);
    }
}

public class TextLayoutTests
{
    // Every character one unit wide, so positions are easy to reason about.
    private static TextLayout Lay(string text, float wrap) =>
        TextLayout.Build(RichText.Plain(text), wrap, 2f, (_, _) => 1f);

    [Fact]
    public void Lines_break_at_spaces()
    {
        var layout = Lay("hello world foo", 10f);
        Assert.Equal(2, layout.Lines.Length);
        Assert.Equal((0, 6), (layout.Lines[0].Start, layout.Lines[0].End));
        Assert.Equal(5f, layout.Lines[0].Width);
        Assert.Equal((6, 15), (layout.Lines[1].Start, layout.Lines[1].End));
        Assert.Equal(0f, layout.X[6]);
        Assert.Equal(9f, layout.Width);
        Assert.Equal(4f, layout.Height);
    }

    [Fact]
    public void A_word_longer_than_a_line_is_cut()
    {
        var layout = Lay("abcdefghijkl", 5f);
        Assert.Equal(3, layout.Lines.Length);
        Assert.Equal(5, layout.Lines[1].Start);
    }

    [Fact]
    public void Line_breaks_in_the_text_are_kept()
    {
        var layout = Lay("one\ntwo", 50f);
        Assert.Equal(2, layout.Lines.Length);
        Assert.Equal((0, 3), (layout.Lines[0].Start, layout.Lines[0].End));
        Assert.Equal(4, layout.Lines[1].Start);
    }

    [Fact]
    public void Points_map_to_the_nearest_gap_between_characters()
    {
        var layout = Lay("hello world foo", 10f);
        Assert.Equal(0, layout.IndexAt(new Vector2(-5f, 1f)));
        Assert.Equal(1, layout.IndexAt(new Vector2(0.6f, 1f)));
        Assert.Equal(6, layout.IndexAt(new Vector2(99f, 1f)));
        Assert.Equal(8, layout.IndexAt(new Vector2(2.2f, 3f)));
        Assert.Equal(15, layout.IndexAt(new Vector2(1f, 99f)));
        Assert.Equal(7, layout.CharAt(new Vector2(1.5f, 3f)));
        Assert.Equal(-1, layout.CharAt(new Vector2(30f, 3f)));
    }

    [Fact]
    public void A_highlight_covers_each_line_it_touches()
    {
        var layout = Lay("hello world foo", 10f);
        var rects = new List<(Vector2 Min, Vector2 Max)>();
        layout.Highlight(3, 9, false, 0.5f, rects);

        Assert.Equal(2, rects.Count);
        Assert.Equal(new Vector2(3f, 0f), rects[0].Min);
        Assert.Equal(new Vector2(6.5f, 2f), rects[0].Max);
        Assert.Equal(new Vector2(0f, 2f), rects[1].Min);
        Assert.Equal(new Vector2(3f, 4f), rects[1].Max);
    }

    [Fact]
    public void Segments_follow_lines_and_runs()
    {
        var layout = TextLayout.Build(RichText.Plain("see example.com now"), 100f, 2f, (_, _) => 1f);
        Assert.Equal(3, layout.Segments.Length);
        Assert.Equal("example.com", layout.Source.Text[layout.Segments[1].Start..layout.Segments[1].End]);
    }

    [Theory]
    [InlineData("hello world", 2, 0, 5)]
    [InlineData("hello world", 5, 5, 6)]
    [InlineData("it's fine", 1, 0, 4)]
    [InlineData("a, b", 1, 1, 2)]
    public void Words_are_found_around_a_position(string text, int at, int start, int end) =>
        Assert.Equal((start, end), TextLayout.WordAt(text, at));
}

public class TextSelectionTests
{
    private static List<ChatMessage> Messages(params string[] texts) => [.. texts.Select(text => new ChatMessage { Text = text })];

    [Fact]
    public void A_selection_made_backwards_is_put_in_order()
    {
        var messages = Messages("first message", "second message", "third message");
        var selection = new TextSelection();
        selection.Start(messages[2], 5);
        selection.Extend(messages[0], 6);

        Assert.True(selection.Resolve(messages, out var range));
        Assert.Equal(new TextSelection.Range(0, 6, 2, 5), range);
        Assert.Equal("message\nsecond message\nthird", selection.Copy(messages, message => RichText.Plain(message.Text)));
    }

    [Fact]
    public void A_click_without_a_drag_selects_nothing()
    {
        var messages = Messages("hello");
        var selection = new TextSelection();
        selection.Start(messages[0], 2);
        Assert.True(selection.IsEmpty);
        Assert.False(selection.Resolve(messages, out _));
    }

    [Fact]
    public void A_message_that_has_gone_ends_the_selection()
    {
        var messages = Messages("one", "two");
        var selection = new TextSelection();
        selection.Select(messages[0], 0, 3);
        messages.RemoveAt(0);

        Assert.False(selection.Resolve(messages, out _));
        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void Each_message_knows_how_much_of_it_is_selected()
    {
        var range = new TextSelection.Range(1, 4, 3, 2);
        Assert.False(range.Slice(0, 10, out _, out _, out _));
        Assert.True(range.Slice(1, 10, out var start, out var end, out var continues));
        Assert.Equal((4, 10, true), (start, end, continues));
        Assert.True(range.Slice(2, 10, out start, out end, out continues));
        Assert.Equal((0, 10, true), (start, end, continues));
        Assert.True(range.Slice(3, 10, out start, out end, out continues));
        Assert.Equal((0, 2, false), (start, end, continues));
    }
}

public class SearchTests
{
    [Fact]
    public void Snippets_centre_on_the_match()
    {
        var text = string.Join(' ', Enumerable.Repeat("filler", 30)) + " the glamour prism is here " + string.Join(' ', Enumerable.Repeat("more", 30));
        var at = MessageSearch.Find(text, "PRISM");
        var snippet = MessageSearch.Snippet(text, at, 5, 20, 60, out var match);

        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.Equal("prism", snippet.Substring(match, 5));
        Assert.True(snippet.Length <= 62);
    }

    [Fact]
    public void Memory_search_finds_messages_newest_first()
    {
        var store = new ConversationStore(null, () => 1000);
        store.LoadCharacter(1, "Me", "Jenova", saveHistory: false);
        var alice = store.OpenTell("Alice Smith", 40, "Jenova");
        store.Add(alice, new ChatMessage { Timestamp = 10, Sender = "Alice Smith", Text = "got the Prism?" });
        store.Add(alice, new ChatMessage { Timestamp = 20, Sender = "Alice Smith", Text = "nothing here" });
        store.Add(alice, new ChatMessage { Timestamp = 30, Flags = MessageFlags.Outgoing, Text = "two prisms, yes" });

        List<SearchHit>? hits = null;
        store.Search([alice], "prism", 10, found => hits = found);

        Assert.NotNull(hits);
        Assert.Equal([30L, 10L], hits!.Select(hit => hit.Timestamp));
        Assert.True(hits[0].Outgoing);
    }

    [Fact]
    public void History_search_reads_the_whole_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "parley-search-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var history = new HistoryStore(root);
            var store = new ConversationStore(history, () => 1000) { PageSize = 2, MaxLoadedMessages = 100 };
            store.LoadCharacter(7, "Me", "Jenova", saveHistory: true);
            history.Flush();
            store.Tick();

            var bob = store.OpenTell("Bob Jones", 40, "Jenova");
            for (var i = 0; i < 20; i++)
                store.Add(bob, new ChatMessage { Timestamp = 100 + i, Sender = "Bob Jones", Text = i == 3 ? "the café is open" : $"line {i}" });

            // Only the newest page in memory: the match is on disk alone.
            bob.Messages.RemoveRange(0, 18);

            List<SearchHit>? hits = null;
            store.Search([bob], "café", 10, found => hits = found);
            history.Flush();
            store.Tick();

            var hit = Assert.Single(hits!);
            Assert.Equal(103L, hit.Timestamp);
            Assert.Equal(bob.Key, hit.Key);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

public class NewestIncomingTests
{
    // Reading the index queues the scan behind it, so one flush is not enough.
    private static void Settle(HistoryStore history, ConversationStore store)
    {
        for (var i = 0; i < 4; i++)
        {
            history.Flush();
            store.Tick();
        }
    }

    [Fact]
    public void Picks_the_conversation_someone_last_spoke_in()
    {
        var store = new ConversationStore(null, () => 1000);
        store.LoadCharacter(1, "Me", "Jenova", saveHistory: false);
        var alice = store.OpenTell("Alice Smith", 40, "Jenova");
        var bob = store.OpenTell("Bob Jones", 40, "Jenova");
        var company = store.GetLinkshell(ChannelGroup.FreeCompany, "Lanterns", 1);

        store.Add(alice, new ChatMessage { Timestamp = 100, Sender = "Alice Smith", Text = "hi" });
        store.Add(bob, new ChatMessage { Timestamp = 200, Sender = "Bob Jones", Text = "hello" });
        store.Add(alice, new ChatMessage { Timestamp = 300, Flags = MessageFlags.Outgoing, Text = "my reply does not count" });
        Assert.Same(bob, store.NewestIncoming());

        store.Add(company, new ChatMessage { Timestamp = 400, Sender = "Pell", Text = "fc chat" });
        Assert.Same(company, store.NewestIncoming());

        store.SetMuted(company, true);
        Assert.Same(bob, store.NewestIncoming());
    }

    [Fact]
    public void Survives_a_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "parley-incoming-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var history = new HistoryStore(root))
            {
                var store = new ConversationStore(history, () => 1000);
                store.LoadCharacter(7, "Me", "Jenova", saveHistory: true);
                Settle(history, store);

                var alice = store.OpenTell("Alice Smith", 40, "Jenova");
                store.Add(alice, new ChatMessage { Timestamp = 500, Sender = "Alice Smith", Text = "hi" });
                store.Add(alice, new ChatMessage { Timestamp = 600, Flags = MessageFlags.Outgoing, Text = "hey" });
                store.FlushIndex();
                history.Flush();
            }

            using (var history = new HistoryStore(root))
            {
                var store = new ConversationStore(history, () => 2000) { ResolveWorld = _ => 40 };
                store.LoadCharacter(7, "Me", "Jenova", saveHistory: true);
                Settle(history, store);

                var alice = Assert.IsType<Conversation>(store.NewestIncoming());
                Assert.Equal(500L, alice.LastIncoming);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

public class ViewerTests
{
    [Fact]
    public void A_conversation_shown_in_any_window_is_read_as_it_arrives()
    {
        var store = new ConversationStore(null, () => 1000);
        store.LoadCharacter(1, "Me", "Jenova", saveHistory: false);
        var company = store.GetLinkshell(ChannelGroup.FreeCompany, "Lanterns", 1);

        store.SetViewed(1 + (int)ChannelGroup.FreeCompany, company.Key);
        store.Add(company, new ChatMessage { Sender = "Pell", Text = "hello" });
        Assert.Equal(0, company.Unread);
        Assert.True(store.IsViewed(company.Key));

        store.SetViewed(1 + (int)ChannelGroup.FreeCompany, null);
        store.Add(company, new ChatMessage { Sender = "Pell", Text = "again" });
        Assert.Equal(1, company.Unread);
    }
}

public class NewSettingsTests
{
    [Fact]
    public void Popped_out_kinds_are_remembered_once_each()
    {
        var configuration = new Configuration();
        configuration.SetPoppedOut(ChannelGroup.FreeCompany, true);
        configuration.SetPoppedOut(ChannelGroup.FreeCompany, true);
        Assert.True(configuration.IsPoppedOut(ChannelGroup.FreeCompany));
        Assert.Single(configuration.PoppedOut);

        configuration.SetPoppedOut(ChannelGroup.FreeCompany, false);
        Assert.False(configuration.IsPoppedOut(ChannelGroup.FreeCompany));
    }

    [Fact]
    public void Recent_symbols_stay_short_and_most_recent_first()
    {
        var configuration = new Configuration();
        for (var i = 0; i < 20; i++) configuration.UsedSymbol(((char)('a' + i)).ToString());
        configuration.UsedSymbol("c");

        Assert.Equal(Configuration.MaxRecentSymbols, configuration.RecentSymbols.Count);
        Assert.Equal("c", configuration.RecentSymbols[0]);
        Assert.Equal(1, configuration.RecentSymbols.Count(symbol => symbol == "c"));
    }

    [Fact]
    public void Defaults_suit_the_game()
    {
        var configuration = new Configuration();
        Assert.True(configuration.ReleaseKeyboardOnEnter);
        Assert.True(configuration.NativeItemTooltips);
        Assert.True(configuration.ShowFriendStatus);
        Assert.Empty(configuration.PoppedOut);
    }
}

public class SymbolTests
{
    [Fact]
    public void Every_symbol_is_one_character_with_a_name()
    {
        foreach (var group in Symbols.Groups)
        {
            Assert.NotEmpty(group.Items);
            Assert.Equal(group.Items.Length, group.Items.Select(item => item.Text).Distinct().Count());
            foreach (var symbol in group.Items)
            {
                Assert.Single(symbol.Text);
                Assert.False(char.IsSurrogate(symbol.Text[0]));
                Assert.False(string.IsNullOrWhiteSpace(symbol.Name));
            }
        }
    }
}

public class FriendStatusBookTests
{
    private static readonly FriendStatus Online = new(true, true, false, false, false, 40, "Jenova");
    private static readonly FriendStatus InDuty = new(true, true, false, false, true, 40, "Jenova");

    [Fact]
    public void A_friend_missing_from_one_reading_keeps_their_status()
    {
        var book = new FriendStatusBook { KeepMissingMs = 20_000 };
        book.Update([("Y'lani Sorel", 40, Online)], now: 1_000);

        // The game empties its list while fetching it again.
        book.Update([], now: 2_000);
        Assert.Equal(Online, book.Get("y'lani sorel", 40));

        book.Update([("Y'lani Sorel", 40, InDuty)], now: 3_000);
        Assert.Equal(InDuty, book.Get("Y'lani Sorel", 40));
    }

    [Fact]
    public void Someone_gone_for_longer_than_a_fetch_is_dropped()
    {
        var book = new FriendStatusBook { KeepMissingMs = 20_000 };
        book.Update([("Y'lani Sorel", 40, Online), ("Mira Thorne", 40, Online)], now: 1_000);
        book.Update([("Mira Thorne", 40, Online)], now: 30_000);

        Assert.Equal(FriendStatus.None, book.Get("Y'lani Sorel", 40));
        Assert.True(book.Get("Mira Thorne", 40).IsFriend);
        Assert.Equal(1, book.Count);
    }

    [Fact]
    public void Home_worlds_keep_namesakes_apart()
    {
        var book = new FriendStatusBook();
        book.Update([("Mira Thorne", 40, Online)], now: 1_000);
        Assert.Equal(FriendStatus.None, book.Get("Mira Thorne", 73));
    }
}

public class FriendStatusTests
{
    [Fact]
    public void Describes_where_a_friend_is()
    {
        Assert.Equal(string.Empty, FriendStatus.None.Describe(40));
        Assert.Equal("Offline", new FriendStatus(true, false, false, false, false, 0, string.Empty).Describe(40));
        Assert.Equal("In a duty", new FriendStatus(true, true, false, false, true, 40, "Jenova").Describe(40));
        Assert.Equal("Busy on Gilgamesh", new FriendStatus(true, true, true, false, false, 63, "Gilgamesh").Describe(40));
        Assert.Equal("In a duty\nSecond Board of the Unbroken",
            new FriendStatus(true, true, false, false, true, 40, "Jenova") { Place = "Second Board of the Unbroken" }.DescribeWithPlace(40));
        Assert.Equal("Offline", new FriendStatus(true, false, false, false, false, 0, string.Empty) { Place = "Limsa Lominsa" }.DescribeWithPlace(40));
        Assert.True(new FriendStatus(true, true, true, false, false, 40, "Jenova").Unreachable);
        Assert.True(new FriendStatus(true, true, false, false, true, 40, "Jenova").Unreachable);
        Assert.False(new FriendStatus(true, true, false, true, false, 40, "Jenova").Unreachable);
    }
}

public class ExtraBytesTests
{
    [Fact]
    public void Links_that_grow_when_sent_are_allowed_for_when_splitting()
    {
        var store = new ConversationStore(null, () => 1000);
        store.LoadCharacter(1, "Me", "Jenova", saveHistory: false);
        var alice = store.OpenTell("Alice Smith", 40, "Jenova");

        var sent = new List<string>();
        var queue = new OutgoingQueue(line => { sent.Add(line); return true; }, () => 0) { ExtraBytes = text => text.Contains('#') ? 300 : 0 };

        var text = string.Join(' ', Enumerable.Repeat("words", 40)) + " #";
        Assert.Equal(SendOutcome.Sent, queue.Send(alice, text));
        queue.Update();
        Assert.True(queue.QueuedCount >= 1, "a text that fits on its own is split once its links are counted");
    }
}
