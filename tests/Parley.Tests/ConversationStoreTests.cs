using Parley.Core;
using Parley.Core.History;
using Xunit;

namespace Parley.Tests;

public class ConversationStoreTests : IDisposable
{
    private const ulong Me = 0x00112233AABBCCDD;

    private readonly TempDirectory temp = new();
    private readonly HistoryStore history;
    private long now = 1_700_000_000_000;

    public ConversationStoreTests()
    {
        history = new HistoryStore(temp.Path);
    }

    public void Dispose()
    {
        history.Dispose();
        temp.Dispose();
    }

    private ConversationStore NewStore() => new(history, () => now)
    {
        ResolveWorld = name => name == "Gilgamesh" ? (ushort)63 : (ushort)0,
    };

    /// <summary>A store with a character loaded and its index applied, saving history.</summary>
    private ConversationStore Loaded()
    {
        var store = NewStore();
        store.LoadCharacter(Me, "Me Myself", "Gilgamesh", saveHistory: true);
        Settle(store);
        return store;
    }

    /// <summary>A store that keeps everything in memory, for tests that are not about the disk.</summary>
    private ConversationStore InMemory()
    {
        var store = new ConversationStore(null, () => now);
        store.LoadCharacter(Me, "Me Myself", "Gilgamesh", saveHistory: false);
        return store;
    }

    /// <summary>Lets the disk catch up and hands the results to the store.</summary>
    private void Settle(ConversationStore store)
    {
        // Twice: reading the index queues the scan that follows it.
        history.Flush();
        history.Flush();
        store.Tick();
    }

    private ChatMessage Incoming(string text, string sender = "Alice Smith") =>
        new() { Timestamp = ++now, Sender = sender, SenderWorld = 63, Text = text };

    private ChatMessage Outgoing(string text) =>
        new() { Timestamp = ++now, Flags = MessageFlags.Outgoing, Sender = "Me Myself", SenderWorld = 63, Text = text };

    private static Conversation Alice(ConversationStore store) => store.OpenTell("Alice Smith", 63, "Gilgamesh");

    private static string[] Texts(Conversation conversation) => conversation.Messages.Select(m => m.Text).ToArray();

    // ---- Unread ----

    [Fact]
    public void Incoming_messages_count_and_a_reply_clears_them()
    {
        var store = InMemory();
        var alice = Alice(store);

        store.Add(alice, Incoming("hi"));
        store.Add(alice, Incoming("there"));

        Assert.Equal(2, alice.Unread);
        Assert.Equal(2, store.Unread(ChannelGroup.Tell));
        Assert.Equal(0, store.Unread(ChannelGroup.Linkshell));
        Assert.Equal(2, store.TotalUnread);
        Assert.Equal(alice.Messages[0].Timestamp, alice.FirstUnreadTimestamp);

        store.Add(alice, Outgoing("hello"));

        Assert.Equal(0, alice.Unread);
        Assert.Equal(0, store.TotalUnread);
    }

    [Fact]
    public void A_conversation_on_screen_is_read_as_it_arrives()
    {
        var store = InMemory();
        var alice = Alice(store);
        store.ViewedKey = alice.Key;

        store.Add(alice, Incoming("hi"));

        Assert.Equal(0, alice.Unread);
        Assert.Single(alice.Messages);
    }

    [Fact]
    public void Muted_conversations_record_messages_but_never_count()
    {
        var store = InMemory();
        var alice = Alice(store);
        store.Add(alice, Incoming("one"));

        store.SetMuted(alice, true);
        store.Add(alice, Incoming("two"));

        Assert.Equal(0, alice.Unread);
        Assert.Equal(2, alice.Messages.Count);
        Assert.Equal(0, store.TotalUnread);
    }

    [Fact]
    public void Notices_are_not_unread_and_do_not_change_the_preview()
    {
        var store = InMemory();
        var alice = Alice(store);
        store.ViewedKey = alice.Key;
        store.Add(alice, Incoming("the real last message"));
        store.ViewedKey = null;

        store.AddNotice(alice, "Message could not be sent.", error: true);

        Assert.Equal(0, alice.Unread);
        Assert.Equal("the real last message", alice.LastPreview);
        Assert.True(alice.Messages[^1].IsNotice);
        Assert.True(alice.Messages[^1].IsError);
    }

    [Fact]
    public void Marking_everything_read_reports_how_much_that_was()
    {
        var store = InMemory();
        var alice = Alice(store);
        var hunts = store.GetLinkshell(ChannelGroup.Linkshell, "Hunts", 1);
        store.Add(alice, Incoming("a"));
        store.Add(hunts, Incoming("b", "Bob Jones"));
        store.Add(hunts, Incoming("c", "Bob Jones"));

        Assert.Equal(3, store.MarkAllRead());
        Assert.Equal(0, store.TotalUnread);
        Assert.Equal(0, store.MarkAllRead());
    }

    [Fact]
    public void Reading_keeps_the_new_messages_marker_until_the_conversation_is_left()
    {
        var store = InMemory();
        var alice = Alice(store);
        store.Add(alice, Incoming("a"));
        var marker = alice.FirstUnreadTimestamp;

        store.MarkRead(alice);
        Assert.Equal(0, alice.Unread);
        Assert.Equal(marker, alice.FirstUnreadTimestamp);

        store.ClearUnreadMarker(alice);
        Assert.Equal(0, alice.FirstUnreadTimestamp);
    }

    [Fact]
    public void A_new_run_of_unread_messages_moves_the_marker_to_where_it_starts()
    {
        var store = InMemory();
        var alice = Alice(store);
        store.Add(alice, Incoming("first run"));
        store.MarkRead(alice);

        // Still open on screen, so the old marker was never cleared.
        store.Add(alice, Incoming("second run"));
        store.Add(alice, Incoming("and more"));

        Assert.Equal(2, alice.Unread);
        Assert.Equal(alice.Messages[1].Timestamp, alice.FirstUnreadTimestamp);
    }

    [Fact]
    public void The_revision_moves_with_unread_state_and_not_with_a_draft()
    {
        var store = InMemory();
        var alice = Alice(store);
        var before = store.Revision;

        store.SetDraft(alice, "typing...");
        Assert.Equal(before, store.Revision);

        store.Add(alice, Incoming("hi"));
        Assert.True(store.Revision > before);
    }

    // ---- Closing and ordering ----

    [Fact]
    public void A_closed_conversation_returns_on_the_next_message_unless_muted()
    {
        var store = InMemory();
        var alice = Alice(store);
        var bob = store.OpenTell("Bob Jones", 63, "Gilgamesh");
        store.Add(alice, Incoming("a"));
        store.Add(bob, Incoming("b", "Bob Jones"));
        store.SetMuted(bob, true);

        store.Close(alice);
        store.Close(bob);
        Assert.Empty(store.InGroup(ChannelGroup.Tell));
        Assert.Equal(0, store.TotalUnread);

        store.Add(alice, Incoming("again"));
        store.Add(bob, Incoming("again", "Bob Jones"));

        Assert.Equal([alice], store.InGroup(ChannelGroup.Tell));
        Assert.True(bob.Closed);

        // Writing to a muted, closed conversation is asking to see it again.
        store.Add(bob, Outgoing("fine"));
        Assert.False(bob.Closed);
    }

    [Fact]
    public void Tells_are_listed_most_recent_first_with_pinned_ones_on_top()
    {
        var store = InMemory();
        var alice = Alice(store);
        var bob = store.OpenTell("Bob Jones", 63, "Gilgamesh");
        var carol = store.OpenTell("Carol White", 40, "Jenova");
        store.Add(alice, Incoming("1"));
        store.Add(bob, Incoming("2", "Bob Jones"));
        store.Add(carol, Incoming("3", "Carol White"));

        Assert.Equal([carol, bob, alice], store.InGroup(ChannelGroup.Tell));

        store.SetPinned(alice, true);
        Assert.Equal([alice, carol, bob], store.InGroup(ChannelGroup.Tell));

        store.Add(bob, Incoming("4", "Bob Jones"));
        Assert.Equal([alice, bob, carol], store.InGroup(ChannelGroup.Tell));
    }

    [Fact]
    public void A_name_from_the_game_corrects_the_capitalisation_of_one_typed_by_hand()
    {
        var store = InMemory();
        var typed = store.OpenTell("Alice smith", 63, "Gilgamesh");

        var fromGame = store.OpenTell("Alice Smith", 63, "Gilgamesh", authoritative: true);

        Assert.Same(typed, fromGame);
        Assert.Equal("Alice Smith", typed.Title);
    }

    [Fact]
    public void Finding_a_tell_by_part_of_a_name_prefers_the_most_recent()
    {
        var store = InMemory();
        var alice = Alice(store);
        var alicia = store.OpenTell("Alicia Stone", 40, "Jenova");
        store.Add(alice, Incoming("1"));
        store.Add(alicia, Incoming("2", "Alicia Stone"));

        Assert.Same(alicia, store.FindTellByName("ali"));
        Assert.Same(alice, store.FindTellByName("smith"));
        Assert.Same(alicia, store.FindTellByName("@jenova"));
        Assert.Null(store.FindTellByName("zed"));
    }

    // ---- Linkshell slots ----

    [Fact]
    public void Linkshells_follow_their_names_when_slots_change()
    {
        var store = InMemory();
        store.SetSlots(ChannelGroup.Linkshell, ["Hunts", "Crafters", "", "", "", "", "", ""]);
        var hunts = store.Find(ConversationKey.ForLinkshell(ChannelGroup.Linkshell, "Hunts"))!;
        var crafters = store.Find(ConversationKey.ForLinkshell(ChannelGroup.Linkshell, "Crafters"))!;
        store.Add(hunts, Incoming("S rank up", "Bob Jones"));

        Assert.Equal(1, hunts.Slot);
        Assert.Equal(2, crafters.Slot);
        Assert.Equal([hunts, crafters], store.InGroup(ChannelGroup.Linkshell));

        // Reordered in game: same linkshells, swapped slots.
        store.SetSlots(ChannelGroup.Linkshell, ["Crafters", "Hunts", "", "", "", "", "", ""]);

        Assert.Equal(2, hunts.Slot);
        Assert.Equal(1, crafters.Slot);
        Assert.Equal(["S rank up"], Texts(hunts));
        Assert.Equal([crafters, hunts], store.InGroup(ChannelGroup.Linkshell));
    }

    [Fact]
    public void Leaving_a_linkshell_keeps_its_history_and_stops_it_being_written_to()
    {
        var store = InMemory();
        store.SetSlots(ChannelGroup.Linkshell, ["Hunts", "Crafters", "", "", "", "", "", ""]);
        var hunts = store.Find(ConversationKey.ForLinkshell(ChannelGroup.Linkshell, "Hunts"))!;
        store.Add(hunts, Incoming("bye", "Bob Jones"));

        store.SetSlots(ChannelGroup.Linkshell, ["", "Crafters", "", "", "", "", "", ""]);

        Assert.Equal(0, hunts.Slot);
        Assert.False(hunts.CanSend);
        Assert.Equal(["bye"], Texts(hunts));
        // Still listed, underneath the ones the character is in.
        Assert.Equal(hunts, store.InGroup(ChannelGroup.Linkshell)[^1]);
    }

    [Fact]
    public void The_same_name_is_a_different_conversation_as_a_cross_world_linkshell()
    {
        var store = InMemory();
        var local = store.GetLinkshell(ChannelGroup.Linkshell, "Hunts", 1);
        var cross = store.GetLinkshell(ChannelGroup.CrossWorld, "Hunts", 1);

        Assert.NotSame(local, cross);
        Assert.NotEqual(local.File, cross.File);
    }

    [Fact]
    public void A_message_that_beats_the_linkshell_name_is_folded_in_when_the_name_arrives()
    {
        var store = Loaded();
        var placeholder = store.GetLinkshell(ChannelGroup.Linkshell, string.Empty, 3);
        store.Add(placeholder, Incoming("early bird", "Bob Jones"));
        Assert.Equal("Linkshell 3", placeholder.Title);

        store.SetSlots(ChannelGroup.Linkshell, ["", "", "Hunts", "", "", "", "", ""]);

        var hunts = Assert.Single(store.InGroup(ChannelGroup.Linkshell));
        Assert.Equal("Hunts", hunts.Title);
        Assert.Equal(3, hunts.Slot);
        Assert.Equal(["early bird"], Texts(hunts));
        Assert.Equal(1, hunts.Unread);
        Assert.Null(store.Find(placeholder.Key));

        // And it reached the linkshell's own file, not a file named after a slot.
        history.Flush();
        Assert.True(File.Exists(temp.File($"{store.CharacterDir}/linkshells/Hunts.jsonl")));
        Assert.Single(Directory.GetFiles(temp.File($"{store.CharacterDir}/linkshells")));
    }

    // ---- Persistence ----

    [Fact]
    public void Conversations_survive_a_relog_with_their_unread_counts_pins_and_drafts()
    {
        var first = Loaded();
        var alice = Alice(first);
        first.Add(alice, Incoming("one"));
        first.Add(alice, Incoming("two"));
        first.SetPinned(alice, true);
        first.SetDraft(alice, "half a thought");
        first.UnloadCharacter();

        var second = Loaded();
        var restored = second.Find(alice.Key);

        Assert.NotNull(restored);
        Assert.NotSame(alice, restored);
        Assert.Equal("Alice Smith", restored.Title);
        Assert.Equal("Gilgamesh", restored.WorldName);
        Assert.Equal(2, restored.Unread);
        Assert.True(restored.Pinned);
        Assert.Equal("half a thought", restored.Draft);
        Assert.Equal("two", restored.LastPreview);
        Assert.Equal(2, second.Unread(ChannelGroup.Tell));

        // Messages are not read until the conversation is opened.
        Assert.Empty(restored.Messages);
        second.EnsureHistory(restored);
        Settle(second);
        Assert.Equal(["one", "two"], Texts(restored));
        Assert.True(restored.ReachedStart);
    }

    [Fact]
    public void History_pages_in_without_duplicating_what_arrived_in_the_meantime()
    {
        var first = Loaded();
        var alice = Alice(first);
        for (var i = 1; i <= 10; i++) first.Add(alice, Incoming($"m{i}"));
        first.UnloadCharacter();

        var second = Loaded();
        second.PageSize = 4;
        var restored = second.Find(alice.Key)!;

        second.Add(restored, Incoming("new 1"));   // before its history was ever read
        second.EnsureHistory(restored);
        second.Add(restored, Incoming("new 2"));   // while that read is still on its way
        Settle(second);

        Assert.Equal(["m7", "m8", "m9", "m10", "new 1", "new 2"], Texts(restored));
        Assert.False(restored.ReachedStart);

        Assert.True(second.LoadOlder(restored));
        Assert.False(second.LoadOlder(restored));   // one page at a time
        Settle(second);
        Assert.Equal(["m3", "m4", "m5", "m6", "m7", "m8", "m9", "m10", "new 1", "new 2"], Texts(restored));

        Assert.True(second.LoadOlder(restored));
        Settle(second);
        Assert.Equal(12, restored.Messages.Count);
        Assert.Equal("m1", restored.Messages[0].Text);
        Assert.True(restored.ReachedStart);
        Assert.False(second.LoadOlder(restored));
    }

    [Fact]
    public void Inserting_older_messages_is_signalled_so_a_view_can_hold_its_place()
    {
        var first = Loaded();
        var alice = Alice(first);
        for (var i = 1; i <= 3; i++) first.Add(alice, Incoming($"m{i}"));
        first.UnloadCharacter();

        var second = Loaded();
        var restored = second.Find(alice.Key)!;
        var before = restored.PrependGeneration;

        second.EnsureHistory(restored);
        Settle(second);

        Assert.NotEqual(before, restored.PrependGeneration);
    }

    [Fact]
    public void Memory_is_trimmed_and_paging_still_lines_up_afterwards()
    {
        var store = Loaded();
        store.MaxLoadedMessages = 12;
        store.PageSize = 5;
        var alice = Alice(store);

        for (var i = 1; i <= 80; i++) store.Add(alice, Incoming($"m{i}"));

        Assert.InRange(alice.Messages.Count, 1, 12);
        Assert.Equal("m80", alice.Messages[^1].Text);
        Assert.False(alice.ReachedStart);

        store.ViewedKey = alice.Key;
        store.EnsureHistory(alice);
        Settle(store);
        store.LoadOlder(alice);
        Settle(store);

        // Whatever was trimmed and paged back, the result is one unbroken run.
        var numbers = alice.Messages.Select(m => int.Parse(m.Text[1..])).ToArray();
        Assert.Equal(Enumerable.Range(numbers[0], numbers.Length), numbers);
        Assert.Equal(80, numbers[^1]);
        Assert.True(numbers.Length > 12);
    }

    [Fact]
    public void A_session_that_ends_before_the_index_was_read_does_not_erase_it()
    {
        var first = Loaded();
        var alice = Alice(first);
        first.Add(alice, Incoming("remember me"));
        first.UnloadCharacter();
        history.Flush();

        // Logs in and straight out again: the index read never gets applied.
        var second = NewStore();
        second.LoadCharacter(Me, "Me Myself", "Gilgamesh", saveHistory: true);
        var bob = second.OpenTell("Bob Jones", 63, "Gilgamesh");
        second.Add(bob, Incoming("hey", "Bob Jones"));
        second.UnloadCharacter();
        history.Flush();

        var third = Loaded();

        Assert.NotNull(third.Find(alice.Key));
        // Bob never made it into an index, but his file is there to be found.
        Assert.NotNull(third.Find(bob.Key));
    }

    [Fact]
    public void Messages_that_arrive_before_the_index_add_to_what_was_already_unread()
    {
        var first = Loaded();
        var alice = Alice(first);
        first.Add(alice, Incoming("one"));
        first.Add(alice, Incoming("two"));
        first.UnloadCharacter();

        var second = NewStore();
        second.LoadCharacter(Me, "Me Myself", "Gilgamesh", saveHistory: true);
        var early = Alice(second);
        second.Add(early, Incoming("three"));
        Assert.Equal(1, early.Unread);

        Settle(second);

        Assert.Same(early, second.Find(alice.Key));
        Assert.Equal(3, early.Unread);
    }

    [Fact]
    public void A_lost_index_is_rebuilt_from_the_history_files()
    {
        var first = Loaded();
        var alice = Alice(first);
        var hunts = first.GetLinkshell(ChannelGroup.Linkshell, "Hunts", 1);
        first.Add(alice, Incoming("one"));
        first.Add(hunts, Incoming("two", "Bob Jones"));
        first.UnloadCharacter();
        history.Flush();
        File.Delete(temp.File($"{first.CharacterDir ?? Me.ToString("X16")}/index.json"));

        var second = Loaded();

        var restoredAlice = second.Find(alice.Key);
        Assert.NotNull(restoredAlice);
        Assert.Equal((ushort)63, restoredAlice.WorldId);
        Assert.True(restoredAlice.LastActivity > 0);
        Assert.NotNull(second.Find(hunts.Key));

        second.EnsureHistory(restoredAlice);
        Settle(second);
        Assert.Equal(["one"], Texts(restoredAlice));
    }

    [Fact]
    public void The_index_is_written_once_things_have_been_quiet_for_a_moment()
    {
        var store = Loaded();
        var alice = Alice(store);
        var indexPath = temp.File($"{store.CharacterDir}/index.json");

        store.Add(alice, Incoming("hi"));
        store.Tick();
        history.Flush();
        Assert.False(File.Exists(indexPath));

        now += 2000;
        store.Tick();
        history.Flush();
        Assert.True(File.Exists(indexPath));
    }

    [Fact]
    public void With_history_off_nothing_is_written_and_nothing_pages()
    {
        var store = NewStore();
        store.LoadCharacter(Me, "Me Myself", "Gilgamesh", saveHistory: false);
        var alice = Alice(store);

        store.Add(alice, Incoming("hi"));
        store.EnsureHistory(alice);
        store.UnloadCharacter();
        history.Flush();

        Assert.False(store.SavesHistory);
        Assert.True(alice.ReachedStart);
        Assert.False(Directory.Exists(temp.File(Me.ToString("X16"))));
    }

    [Fact]
    public void Clearing_history_empties_the_conversation_and_its_file()
    {
        var store = Loaded();
        var alice = Alice(store);
        store.Add(alice, Incoming("secret"));
        history.Flush();
        var path = temp.File($"{store.CharacterDir}/{alice.File}");
        Assert.True(File.Exists(path));

        store.ClearHistory(alice);
        history.Flush();

        Assert.Empty(alice.Messages);
        Assert.Equal(0, alice.Unread);
        Assert.False(File.Exists(path));
        Assert.Same(alice, store.Find(alice.Key));
    }

    [Fact]
    public void Forgetting_removes_the_conversation_altogether()
    {
        var store = Loaded();
        var alice = Alice(store);
        store.Add(alice, Incoming("secret"));
        history.Flush();

        store.Forget(alice);
        history.Flush();

        Assert.Null(store.Find(alice.Key));
        Assert.Equal(0, store.TotalUnread);
        Assert.False(File.Exists(temp.File($"{store.CharacterDir}/{alice.File}")));
    }

    [Fact]
    public void Each_character_has_its_own_conversations()
    {
        var store = Loaded();
        var alice = Alice(store);
        store.Add(alice, Incoming("for the first character"));

        store.LoadCharacter(Me + 1, "Other Me", "Jenova", saveHistory: true);
        Settle(store);

        Assert.Null(store.Find(alice.Key));
        Assert.Equal(0, store.TotalUnread);
        Assert.Empty(store.InGroup(ChannelGroup.Tell));

        store.LoadCharacter(Me, "Me Myself", "Gilgamesh", saveHistory: true);
        Settle(store);

        Assert.NotNull(store.Find(alice.Key));
        Assert.Equal(1, store.TotalUnread);
    }

    [Fact]
    public void A_read_that_finishes_after_a_character_change_is_discarded()
    {
        var store = Loaded();
        var alice = Alice(store);
        store.Add(alice, Incoming("one"));
        store.UnloadCharacter();
        store.LoadCharacter(Me, "Me Myself", "Gilgamesh", saveHistory: true);
        Settle(store);
        var restored = store.Find(alice.Key)!;

        store.EnsureHistory(restored);
        store.LoadCharacter(Me + 1, "Other Me", "Jenova", saveHistory: true);
        Settle(store);

        // The page belonged to a conversation that is no longer loaded.
        Assert.Empty(restored.Messages);
    }

    // ---- Status for other plugins ----

    [Fact]
    public void Status_lists_unread_conversations_newest_first_and_caps_the_list()
    {
        var store = InMemory();
        for (var i = 0; i < 12; i++)
        {
            var conversation = store.OpenTell($"Player Number{i}", 63, "Gilgamesh");
            store.Add(conversation, Incoming($"hello {i}", $"Player Number{i}"));
        }
        var hunts = store.GetLinkshell(ChannelGroup.Linkshell, "Hunts", 1);
        store.Add(hunts, Incoming("S rank", "Bob Jones"));
        store.Add(hunts, Incoming("pulling", "Bob Jones"));
        var read = store.OpenTell("Already Read", 63, "Gilgamesh");
        store.Add(read, Outgoing("nothing unread here"));

        var status = store.BuildStatus(windowOpen: true, maxConversations: 5);

        Assert.Equal(1, status.Version);
        Assert.True(status.LoggedIn);
        Assert.True(status.WindowOpen);
        Assert.Equal(12, status.Tells);
        Assert.Equal(2, status.Linkshells);
        Assert.Equal(0, status.CrossWorld);
        Assert.Equal(store.Revision, status.Revision);
        Assert.Equal(5, status.Conversations.Count);

        var newest = status.Conversations[0];
        Assert.Equal(1, newest.Group);
        Assert.Equal("Hunts", newest.Title);
        Assert.Equal(2, newest.Unread);
        Assert.Equal("Bob Jones", newest.Sender);
        Assert.Equal("pulling", newest.Preview);
        Assert.Equal("Player Number11", status.Conversations[1].Title);
        Assert.Equal("Gilgamesh", status.Conversations[1].World);
    }

    [Fact]
    public void Status_at_the_title_screen_says_so()
    {
        var store = new ConversationStore(null, () => now);

        var status = store.BuildStatus(windowOpen: false);

        Assert.False(status.LoggedIn);
        Assert.Empty(status.Conversations);
    }

    [Fact]
    public void A_long_message_is_shortened_for_its_preview()
    {
        var store = InMemory();
        var alice = Alice(store);

        store.Add(alice, Incoming(new string('a', 500)));

        Assert.True(alice.LastPreview.Length < 200);
        Assert.EndsWith("…", alice.LastPreview);
    }
}
