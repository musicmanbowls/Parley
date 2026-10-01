using System.Text;
using System.Text.Json;
using Parley.Core;
using Parley.Core.History;
using Xunit;

namespace Parley.Tests;

public class HistoryStoreTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly List<(string What, Exception Error)> errors = [];
    private readonly HistoryStore store;

    public HistoryStoreTests()
    {
        store = new HistoryStore(temp.Path, (what, error) => errors.Add((what, error)));
    }

    public void Dispose()
    {
        store.Dispose();
        temp.Dispose();
    }

    private static ChatMessage Message(int n, MessageFlags flags = MessageFlags.None) => new()
    {
        Timestamp = 1_700_000_000_000 + n,
        Flags = flags,
        Sender = "Alice Smith",
        SenderWorld = 63,
        Text = $"message {n}",
    };

    private void AppendMany(string file, int count)
    {
        for (var i = 1; i <= count; i++) store.Append(file, Message(i));
    }

    private static int[] Numbers(HistoryPage page) =>
        page.Messages.Select(m => int.Parse(m.Text["message ".Length..])).ToArray();

    [Fact]
    public async Task What_is_appended_comes_back_with_every_field()
    {
        store.Append("c/tells/a.jsonl", new ChatMessage
        {
            Timestamp = 1234567890123,
            Flags = MessageFlags.Outgoing,
            Sender = "Alice Smith",
            SenderWorld = 63,
            Text = "it's \"quoted\" \\ and\ttabbed",
            Rich = [2, 0x13, 3, 0xFF],
        });

        var page = await store.ReadTail("c/tells/a.jsonl", 0, 10);

        var message = Assert.Single(page.Messages);
        Assert.Equal(1234567890123, message.Timestamp);
        Assert.Equal(MessageFlags.Outgoing, message.Flags);
        Assert.Equal("Alice Smith", message.Sender);
        Assert.Equal(63, message.SenderWorld);
        Assert.Equal("it's \"quoted\" \\ and\ttabbed", message.Text);
        Assert.Equal(new byte[] { 2, 0x13, 3, 0xFF }, message.Rich);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Pages_walk_backwards_without_overlap_or_gaps()
    {
        AppendMany("c/tells/a.jsonl", 10);

        var newest = await store.ReadTail("c/tells/a.jsonl", 0, 4);
        var middle = await store.ReadTail("c/tells/a.jsonl", 4, 4);
        var oldest = await store.ReadTail("c/tells/a.jsonl", 8, 4);
        var beyond = await store.ReadTail("c/tells/a.jsonl", 10, 4);

        Assert.Equal([7, 8, 9, 10], Numbers(newest));
        Assert.Equal(4, newest.RawLines);
        Assert.False(newest.ReachedStart);

        Assert.Equal([3, 4, 5, 6], Numbers(middle));
        Assert.False(middle.ReachedStart);

        Assert.Equal([1, 2], Numbers(oldest));
        Assert.Equal(2, oldest.RawLines);
        Assert.True(oldest.ReachedStart);

        Assert.Empty(beyond.Messages);
        Assert.True(beyond.ReachedStart);
    }

    [Fact]
    public async Task A_page_that_ends_exactly_on_the_first_line_knows_it_reached_the_start()
    {
        AppendMany("c/tells/a.jsonl", 8);

        var first = await store.ReadTail("c/tells/a.jsonl", 0, 4);
        var second = await store.ReadTail("c/tells/a.jsonl", 4, 4);

        Assert.False(first.ReachedStart);
        Assert.Equal([1, 2, 3, 4], Numbers(second));
        Assert.True(second.ReachedStart);
    }

    [Fact]
    public async Task Paging_is_right_in_a_file_much_larger_than_the_scan_block()
    {
        // Several hundred KB, so finding the tail has to cross a number of
        // 64 KB blocks. Written in one go; appending 6000 times is slow.
        const int count = 6000;
        var lines = Enumerable.Range(1, count)
            .Select(i => JsonSerializer.Serialize(StoredMessage.From(Message(i)), CoreJson.Lines.StoredMessage));
        Directory.CreateDirectory(temp.File("c/linkshells"));
        File.WriteAllText(temp.File("c/linkshells/big.jsonl"), string.Join('\n', lines) + "\n");
        Assert.True(new FileInfo(temp.File("c/linkshells/big.jsonl")).Length > 4 * 64 * 1024);

        var newest = await store.ReadTail("c/linkshells/big.jsonl", 0, 50);
        var deep = await store.ReadTail("c/linkshells/big.jsonl", 3000, 50);
        var oldest = await store.ReadTail("c/linkshells/big.jsonl", count - 10, 50);

        Assert.Equal(Enumerable.Range(count - 49, 50), Numbers(newest));
        Assert.Equal(Enumerable.Range(count - 3000 - 49, 50), Numbers(deep));
        Assert.Equal(Enumerable.Range(1, 10), Numbers(oldest));
        Assert.True(oldest.ReachedStart);
    }

    [Fact]
    public async Task An_unreadable_line_is_skipped_but_still_counted()
    {
        AppendMany("c/tells/a.jsonl", 2);
        store.Flush();
        File.AppendAllText(temp.File("c/tells/a.jsonl"), "{ this is not json\n");
        store.Append("c/tells/a.jsonl", Message(3));

        var page = await store.ReadTail("c/tells/a.jsonl", 0, 10);

        Assert.Equal([1, 2, 3], Numbers(page));
        // Four raw lines: paging by raw count is what keeps later pages aligned.
        Assert.Equal(4, page.RawLines);
    }

    [Fact]
    public async Task A_line_left_unterminated_by_a_crash_is_closed_off_before_the_next_append()
    {
        AppendMany("c/tells/a.jsonl", 1);
        store.Flush();
        var path = temp.File("c/tells/a.jsonl");
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^1]);

        store.Append("c/tells/a.jsonl", Message(2));
        var page = await store.ReadTail("c/tells/a.jsonl", 0, 10);

        Assert.Equal([1, 2], Numbers(page));
    }

    [Fact]
    public async Task A_missing_file_is_an_empty_history_not_an_error()
    {
        var page = await store.ReadTail("c/tells/nobody.jsonl", 0, 10);

        Assert.Empty(page.Messages);
        Assert.True(page.ReachedStart);
        Assert.Empty(errors);
    }

    [Fact]
    public void Text_is_written_readable_rather_than_escaped()
    {
        store.Append("c/tells/a.jsonl", new ChatMessage { Timestamp = 1, Text = "こんにちは, it's me" });
        store.Flush();

        var raw = File.ReadAllText(temp.File("c/tells/a.jsonl"), Encoding.UTF8);

        Assert.Contains("こんにちは, it's me", raw);
        Assert.Equal(1, raw.Count(ch => ch == '\n'));
    }

    [Fact]
    public void Defaults_are_left_out_of_a_line()
    {
        store.Append("c/tells/a.jsonl", new ChatMessage { Timestamp = 5, Text = "hi" });
        store.Flush();

        Assert.Equal("{\"t\":5,\"m\":\"hi\"}\n", File.ReadAllText(temp.File("c/tells/a.jsonl")));
    }

    [Fact]
    public void The_index_round_trips()
    {
        var index = new HistoryIndex { Character = "Me Myself", World = "Gilgamesh" };
        index.Conversations.Add(new IndexEntry
        {
            Key = "t:Alice Smith@63", Group = 0, Title = "Alice Smith", WorldId = 63, WorldName = "Gilgamesh",
            File = "tells/Alice Smith@Gilgamesh.jsonl", LastActivity = 99, Unread = 3, Pinned = true, Draft = "half a thought",
        });
        store.WriteIndex("c", index);

        HistoryIndex? read = null;
        store.ReadIndex("c", result => read = result);
        store.Flush();

        Assert.NotNull(read);
        Assert.Equal("Me Myself", read.Character);
        var entry = Assert.Single(read.Conversations);
        Assert.Equal("t:Alice Smith@63", entry.Key);
        Assert.Equal(3, entry.Unread);
        Assert.True(entry.Pinned);
        Assert.False(entry.Muted);
        Assert.Equal("half a thought", entry.Draft);
        Assert.False(File.Exists(temp.File("c/index.json.tmp")));
    }

    [Fact]
    public void A_damaged_index_reads_as_absent()
    {
        Directory.CreateDirectory(temp.File("c"));
        File.WriteAllText(temp.File("c/index.json"), "{ \"Version\": ");

        HistoryIndex? read = new();
        store.ReadIndex("c", result => read = result);
        store.Flush();

        Assert.Null(read);
    }

    [Fact]
    public void Scanning_finds_history_files_and_recovers_their_names()
    {
        store.Append("c/tells/Alice Smith@Gilgamesh.jsonl", Message(1));
        store.Append("c/linkshells/Hunts.jsonl", Message(1));
        store.Append($"c/crossworld/{ConversationKey.SafeFileName("what?")}.jsonl", Message(1));

        List<DiscoveredFile> found = [];
        store.Scan("c", result => found = result);
        store.Flush();

        Assert.Equal(3, found.Count);
        var tell = Assert.Single(found, f => f.Group == ChannelGroup.Tell);
        Assert.Equal("Alice Smith", tell.Name);
        Assert.Equal("Gilgamesh", tell.World);
        Assert.Equal("tells/Alice Smith@Gilgamesh.jsonl", tell.File);
        Assert.Equal("Hunts", Assert.Single(found, f => f.Group == ChannelGroup.Linkshell).Name);
        Assert.Equal("what_", Assert.Single(found, f => f.Group == ChannelGroup.CrossWorld).Name);
    }

    [Fact]
    public async Task Pruning_removes_only_what_is_older_than_the_cutoff()
    {
        AppendMany("c/tells/mixed.jsonl", 10);
        AppendMany("c/tells/old.jsonl", 3);
        for (var i = 20; i <= 22; i++) store.Append("c/tells/recent.jsonl", Message(i));
        store.Flush();
        var recentBefore = File.GetLastWriteTimeUtc(temp.File("c/tells/recent.jsonl"));

        var removed = -1;
        store.Prune("c", 1_700_000_000_000 + 6, count => removed = count);
        store.Flush();

        var mixed = await store.ReadTail("c/tells/mixed.jsonl", 0, 50);

        Assert.Equal(5 + 3, removed);
        Assert.Equal([6, 7, 8, 9, 10], Numbers(mixed));
        Assert.False(File.Exists(temp.File("c/tells/old.jsonl")));
        Assert.Equal(recentBefore, File.GetLastWriteTimeUtc(temp.File("c/tells/recent.jsonl")));
    }

    [Fact]
    public void Deleting_removes_the_file()
    {
        store.Append("c/tells/a.jsonl", Message(1));
        store.Delete("c/tells/a.jsonl");
        store.Flush();

        Assert.False(File.Exists(temp.File("c/tells/a.jsonl")));
    }

    [Fact]
    public void A_path_that_climbs_out_of_the_root_is_refused()
    {
        store.Append("../escaped.jsonl", Message(1));
        store.Flush();

        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(temp.Path)!, "escaped.jsonl")));
        Assert.Contains(errors, e => e.Error is InvalidOperationException);
    }

    [Fact]
    public async Task Work_asked_for_after_disposal_completes_empty_instead_of_hanging()
    {
        store.Dispose();

        var page = await store.ReadTail("c/tells/a.jsonl", 0, 10).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(page.Messages);
    }
}
