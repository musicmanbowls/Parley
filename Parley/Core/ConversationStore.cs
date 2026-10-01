using System.Collections.Concurrent;
using Parley.Core.History;
using Parley.Ipc;

namespace Parley.Core;

/// <summary>
/// Every conversation the logged-in character has, what is unread in each,
/// and the bridge between those and the history files.
///
/// Belongs to one thread. The only thing that crosses threads is the queue of
/// finished disk reads, which <see cref="Tick"/> drains.
/// </summary>
public sealed class ConversationStore
{
    /// <summary>How long the index waits after a change before it is written, so typing a draft is one write and not fifty.</summary>
    private const long IndexDebounceMs = 1500;

    private const int PreviewLength = 140;

    /// <summary>Trimming removes this many extra, so a full conversation is not trimmed again on every message.</summary>
    private const int TrimSlack = 50;

    private readonly HistoryStore? history;
    private readonly Func<long> clock;
    private readonly Dictionary<string, Conversation> byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Conversation>[] sorted = [[], [], [], []];
    private readonly bool[] sortedDirty = [true, true, true, true];
    private readonly int[] unread = new int[ChannelGroups.Count];
    private readonly ConcurrentQueue<Action> completed = new();

    private bool unreadDirty = true;
    private bool persist;
    private long indexDirtySince;

    /// <summary>Bumped on every character change so a disk read that finishes late is recognised as stale.</summary>
    private int generation;

    public ConversationStore(HistoryStore? history, Func<long>? clock = null)
    {
        this.history = history;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>Raised after anything that changes what an unread indicator should show.</summary>
    public event Action? Changed;

    /// <summary>Goes up whenever <see cref="Changed"/> fires; lets a poller skip work when nothing has.</summary>
    public int Revision { get; private set; }

    /// <summary>Directory name for the logged-in character, or null at the title screen.</summary>
    public string? CharacterDir { get; private set; }

    public string CharacterName { get; private set; } = string.Empty;
    public string CharacterWorld { get; private set; } = string.Empty;
    public bool HasCharacter => CharacterDir != null;

    /// <summary>False until the saved index has been read; pins, unread counts and older conversations appear when it has.</summary>
    public bool IndexLoaded { get; private set; }

    public int PageSize { get; set; } = 80;
    public int MaxLoadedMessages { get; set; } = 400;

    /// <summary>How many windows can each be showing a conversation at once: the main window and one per kind popped out.</summary>
    public const int MaxViewers = 1 + ChannelGroups.Count;

    /// <summary>What each window is showing and being read; slot 0 is the main window.</summary>
    private readonly string?[] viewed = new string?[MaxViewers];

    /// <summary>
    /// The conversation on screen in the main window right now, if any.
    /// Messages arriving in it are read as they arrive rather than counted.
    /// </summary>
    public string? ViewedKey
    {
        get => viewed[0];
        set => viewed[0] = value;
    }

    /// <summary>Says what one window is showing; see <see cref="ViewedKey"/>. Slot 0 is the main window.</summary>
    public void SetViewed(int viewer, string? key)
    {
        if (viewer is >= 0 and < MaxViewers) viewed[viewer] = key;
    }

    public string? Viewed(int viewer) => viewer is >= 0 and < MaxViewers ? viewed[viewer] : null;

    /// <summary>Whether any window is showing this conversation to someone reading it.</summary>
    public bool IsViewed(string key)
    {
        foreach (var slot in viewed)
        {
            if (slot != null && string.Equals(slot, key, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Turns a world name from a file name back into its id, for rebuilding a lost index. 0 if unknown.</summary>
    public Func<string, ushort>? ResolveWorld { get; set; }

    public long Now => clock();

    /// <summary>Whether this session is writing history at all. Fixed when the character loads.</summary>
    public bool SavesHistory => persist;

    // ------------------------------------------------------------------
    // Character lifetime
    // ------------------------------------------------------------------

    /// <param name="saveHistory">Fixed for the session: a conversation is either entirely on disk or not at all.</param>
    /// <param name="pruneBeforeMs">Delete saved messages older than this first. 0 to keep everything.</param>
    public void LoadCharacter(ulong contentId, string name, string world, bool saveHistory, long pruneBeforeMs = 0)
    {
        UnloadCharacter();

        generation++;
        CharacterDir = contentId.ToString("X16");
        CharacterName = name;
        CharacterWorld = world;
        persist = saveHistory && history != null;
        IndexLoaded = !persist;

        if (persist)
        {
            var mine = generation;
            var directory = CharacterDir;
            if (pruneBeforeMs > 0) history!.Prune(directory, pruneBeforeMs);

            // The index says what it knows; the scan then adds any history
            // file it does not mention. That covers a lost or damaged index,
            // and a session that ended before its index could be written.
            history!.ReadIndex(directory, index =>
                history.Scan(directory, files => completed.Enqueue(() =>
                {
                    if (mine != generation) return;
                    if (index != null) ApplyIndex(index);
                    ApplyScan(files);
                })));
        }

        Bump();
    }

    public void UnloadCharacter()
    {
        if (CharacterDir == null) return;

        FlushIndex();
        generation++;
        byKey.Clear();
        for (var i = 0; i < sorted.Length; i++)
        {
            sorted[i].Clear();
            sortedDirty[i] = true;
        }

        CharacterDir = null;
        CharacterName = string.Empty;
        CharacterWorld = string.Empty;
        IndexLoaded = false;
        Array.Clear(viewed);
        indexDirtySince = 0;
        Bump();
    }

    /// <summary>Applies finished disk reads and writes the index once it has been quiet for a moment.</summary>
    public void Tick()
    {
        while (completed.TryDequeue(out var work)) work();

        if (indexDirtySince != 0 && clock() - indexDirtySince >= IndexDebounceMs)
            FlushIndex();
    }

    // ------------------------------------------------------------------
    // Lookup
    // ------------------------------------------------------------------

    /// <summary>Open conversations of one kind, in the order the sidebar shows them.</summary>
    public IReadOnlyList<Conversation> InGroup(ChannelGroup group)
    {
        var i = (int)group;
        if (!sortedDirty[i]) return sorted[i];

        var list = sorted[i];
        list.Clear();
        foreach (var conversation in byKey.Values)
        {
            if (conversation.Group == group && !conversation.Closed) list.Add(conversation);
        }

        list.Sort(group == ChannelGroup.Tell ? CompareTells : CompareLinkshells);
        sortedDirty[i] = false;
        return list;
    }

    public Conversation? Find(string key) => byKey.GetValueOrDefault(key);

    public IEnumerable<Conversation> All => byKey.Values;

    /// <summary>First tell whose name contains the text, most recent first. Includes closed ones.</summary>
    public Conversation? FindTellByName(string partial)
    {
        Conversation? best = null;
        foreach (var conversation in byKey.Values)
        {
            if (!conversation.IsTell) continue;
            if (!conversation.DisplayName.Contains(partial, StringComparison.OrdinalIgnoreCase)) continue;
            if (best == null || conversation.LastActivity > best.LastActivity) best = conversation;
        }
        return best;
    }

    /// <param name="authoritative">True when the name came from the game, whose capitalisation then replaces ours.</param>
    public Conversation OpenTell(string name, ushort worldId, string worldName, bool authoritative = false)
    {
        var key = ConversationKey.ForTell(name, worldId);
        if (byKey.TryGetValue(key, out var existing))
        {
            if (authoritative && !string.Equals(existing.Title, name, StringComparison.Ordinal))
            {
                existing.Title = name;
                MarkIndexDirty();
            }
            if (existing.WorldName.Length == 0 && worldName.Length > 0) existing.WorldName = worldName;
            return existing;
        }

        var conversation = new Conversation(key, ChannelGroup.Tell, name)
        {
            WorldId = worldId,
            WorldName = worldName,
            File = ConversationKey.FileFor(ChannelGroup.Tell, name, worldName),
        };
        Register(conversation);
        return conversation;
    }

    /// <summary>
    /// The conversation for a linkshell slot. With no name yet (the game
    /// supplies them a moment after login) it is keyed on the slot, and
    /// <see cref="SetSlots"/> folds it into the real one when the name arrives.
    /// Such a placeholder has no file: what it collects is written when it is
    /// folded in, under the linkshell's real name.
    /// </summary>
    public Conversation GetLinkshell(ChannelGroup group, string name, int slot)
    {
        var known = name.Length > 0;
        var key = known ? ConversationKey.ForLinkshell(group, name) : ConversationKey.ForSlot(group, slot);
        if (!byKey.TryGetValue(key, out var conversation))
        {
            conversation = new Conversation(key, group, known ? name : group.SlotLabel(slot))
            {
                File = known ? ConversationKey.FileFor(group, name, string.Empty) : string.Empty,
                Placeholder = !known,
            };
            Register(conversation);
        }

        if (conversation.Slot != slot)
        {
            conversation.Slot = slot;
            sortedDirty[(int)group] = true;
            Bump();
        }
        return conversation;
    }

    /// <summary>
    /// Tells the store which linkshell is in which slot. Slots come and go as
    /// the character joins, leaves and reorders; histories stay with the name.
    /// </summary>
    public void SetSlots(ChannelGroup group, IReadOnlyList<string> names)
    {
        var changed = false;

        foreach (var conversation in byKey.Values)
        {
            if (conversation.Group != group || conversation.Slot == 0) continue;
            var index = conversation.Slot - 1;
            var name = index < names.Count ? names[index] : string.Empty;
            var expected = name.Length > 0
                ? ConversationKey.ForLinkshell(group, name)
                : ConversationKey.ForSlot(group, conversation.Slot);
            if (string.Equals(conversation.Key, expected, StringComparison.OrdinalIgnoreCase)) continue;

            // A placeholder stays where it is until Merge below picks it up;
            // anything else has left this slot.
            if (!conversation.Placeholder) conversation.Slot = 0;
            changed = true;
        }

        for (var i = 0; i < names.Count && i < group.Slots(); i++)
        {
            if (names[i].Length == 0) continue;
            var named = GetLinkshell(group, names[i], i + 1);
            if (byKey.TryGetValue(ConversationKey.ForSlot(group, i + 1), out var placeholder))
            {
                Merge(placeholder, named);
                changed = true;
            }
        }

        if (!changed) return;
        sortedDirty[(int)group] = true;
        Bump();
    }

    // ------------------------------------------------------------------
    // Messages
    // ------------------------------------------------------------------

    public void Add(Conversation conversation, ChatMessage message)
    {
        if (message.Timestamp == 0) message.Timestamp = clock();
        conversation.Messages.Add(message);

        if (!message.IsNotice)
        {
            conversation.LastActivity = message.Timestamp;
            conversation.LastPreview = Preview(message.Text);
            conversation.LastSender = message.IsOutgoing ? string.Empty : message.Sender;
            if (!message.IsOutgoing) conversation.LastIncoming = message.Timestamp;
        }

        var viewed = IsViewed(conversation);
        if (message.IsOutgoing)
        {
            // Answering a conversation is reading it, wherever the answer was typed.
            conversation.Unread = 0;
            conversation.FirstUnreadTimestamp = 0;
        }
        else if (!message.IsNotice && !conversation.Muted && !viewed)
        {
            // The marker sits on the first message of the current unread run.
            // One left over from a run that has since been read is replaced.
            if (conversation.Unread == 0) conversation.FirstUnreadTimestamp = message.Timestamp;
            conversation.Unread++;
        }

        if (conversation.Closed && (message.IsOutgoing || (!message.IsNotice && !conversation.Muted)))
            conversation.Closed = false;

        if (HasFile(conversation))
        {
            history!.Append($"{CharacterDir}/{conversation.File}", message);
            conversation.RawLinesLoaded++;
        }

        if (!viewed) Trim(conversation);

        sortedDirty[(int)conversation.Group] = true;
        MarkIndexDirty();
        Bump();
    }

    /// <summary>Adds a line that nobody said: a send that failed, a history that was cleared.</summary>
    public void AddNotice(Conversation conversation, string text, bool error = false)
    {
        Add(conversation, new ChatMessage
        {
            Flags = MessageFlags.Notice | (error ? MessageFlags.Error : MessageFlags.None),
            Text = text,
        });
    }

    /// <summary>
    /// Clears the unread count. The "new messages" marker is left where it is
    /// so it stays on screen while the conversation is being read;
    /// <see cref="ClearUnreadMarker"/> removes it on the way out.
    /// </summary>
    public void MarkRead(Conversation conversation)
    {
        if (conversation.Unread == 0) return;
        conversation.Unread = 0;
        MarkIndexDirty();
        Bump();
    }

    public void ClearUnreadMarker(Conversation conversation)
    {
        if (conversation.Unread == 0) conversation.FirstUnreadTimestamp = 0;
    }

    /// <returns>How many unread messages that cleared.</returns>
    public int MarkAllRead()
    {
        var cleared = 0;
        foreach (var conversation in byKey.Values)
        {
            cleared += conversation.Unread;
            conversation.Unread = 0;
            conversation.FirstUnreadTimestamp = 0;
        }

        if (cleared > 0)
        {
            MarkIndexDirty();
            Bump();
        }
        return cleared;
    }

    public void SetPinned(Conversation conversation, bool pinned)
    {
        if (conversation.Pinned == pinned) return;
        conversation.Pinned = pinned;
        sortedDirty[(int)conversation.Group] = true;
        MarkIndexDirty();
        Bump();
    }

    public void SetMuted(Conversation conversation, bool muted)
    {
        if (conversation.Muted == muted) return;
        conversation.Muted = muted;
        if (muted)
        {
            conversation.Unread = 0;
            conversation.FirstUnreadTimestamp = 0;
        }
        MarkIndexDirty();
        Bump();
    }

    /// <summary>Hides a conversation from the sidebar. Its history is kept and it returns on the next message.</summary>
    public void Close(Conversation conversation)
    {
        conversation.Closed = true;
        conversation.Unread = 0;
        conversation.FirstUnreadTimestamp = 0;
        sortedDirty[(int)conversation.Group] = true;
        MarkIndexDirty();
        Bump();
    }

    public void Reopen(Conversation conversation)
    {
        if (!conversation.Closed) return;
        conversation.Closed = false;
        sortedDirty[(int)conversation.Group] = true;
        MarkIndexDirty();
        Bump();
    }

    public void SetDraft(Conversation conversation, string draft)
    {
        if (conversation.Draft == draft) return;
        conversation.Draft = draft;
        MarkIndexDirty();
    }

    /// <summary>Deletes a conversation's messages, in memory and on disk, and keeps the conversation.</summary>
    public void ClearHistory(Conversation conversation)
    {
        conversation.Messages.Clear();
        conversation.RawLinesLoaded = 0;
        conversation.ReachedStart = true;
        conversation.History = HistoryState.Loaded;
        conversation.Unread = 0;
        conversation.FirstUnreadTimestamp = 0;
        conversation.LastPreview = string.Empty;
        conversation.LastSender = string.Empty;
        conversation.PrependGeneration++;
        if (HasFile(conversation)) history!.Delete($"{CharacterDir}/{conversation.File}");
        MarkIndexDirty();
        Bump();
    }

    /// <summary>Deletes a conversation outright. A linkshell the character is still in comes straight back, empty.</summary>
    public void Forget(Conversation conversation)
    {
        if (!byKey.Remove(conversation.Key)) return;
        if (HasFile(conversation)) history!.Delete($"{CharacterDir}/{conversation.File}");
        Rename(conversation.Key, null);
        sortedDirty[(int)conversation.Group] = true;
        MarkIndexDirty();
        Bump();
    }

    /// <summary>
    /// Drops the oldest messages from memory once a conversation is over the
    /// limit. They stay on disk and page back in on request.
    /// </summary>
    public void Trim(Conversation conversation)
    {
        // A page being read was asked for relative to what is in memory now;
        // trimming underneath it would leave a hole where the two meet.
        if (conversation.History == HistoryState.Loading) return;

        var excess = conversation.Messages.Count - MaxLoadedMessages;
        if (excess <= 0) return;

        excess = Math.Min(excess + Math.Min(TrimSlack, MaxLoadedMessages / 4), conversation.Messages.Count - 1);
        conversation.Messages.RemoveRange(0, excess);
        if (!HasFile(conversation)) return;

        conversation.RawLinesLoaded = Math.Max(0, conversation.RawLinesLoaded - excess);
        conversation.ReachedStart = false;
    }

    // ------------------------------------------------------------------
    // Unread
    // ------------------------------------------------------------------

    public int Unread(ChannelGroup group)
    {
        if (unreadDirty)
        {
            Array.Clear(unread);
            foreach (var conversation in byKey.Values) unread[(int)conversation.Group] += conversation.Unread;
            unreadDirty = false;
        }
        return unread[(int)group];
    }

    public int TotalUnread =>
        Unread(ChannelGroup.Tell) + Unread(ChannelGroup.Linkshell) + Unread(ChannelGroup.CrossWorld) + Unread(ChannelGroup.FreeCompany);

    /// <summary>The conversation someone last said something in, leaving out muted and closed ones. Null if there is none.</summary>
    public Conversation? NewestIncoming()
    {
        Conversation? best = null;
        foreach (var conversation in byKey.Values)
        {
            if (conversation.LastIncoming == 0 || conversation.Muted || conversation.Closed) continue;
            if (best == null || conversation.LastIncoming > best.LastIncoming) best = conversation;
        }
        return best;
    }

    /// <summary>The conversation with unread messages that was active most recently.</summary>
    public Conversation? NewestUnread(ChannelGroup? group = null)
    {
        Conversation? best = null;
        foreach (var conversation in byKey.Values)
        {
            if (conversation.Unread == 0) continue;
            if (group != null && conversation.Group != group) continue;
            if (best == null || conversation.LastActivity > best.LastActivity) best = conversation;
        }
        return best;
    }

    public StatusSnapshot BuildStatus(bool windowOpen, int maxConversations = 8)
    {
        var snapshot = new StatusSnapshot
        {
            Version = ParleyIpc.Version,
            Revision = Revision,
            LoggedIn = HasCharacter,
            WindowOpen = windowOpen,
            Tells = Unread(ChannelGroup.Tell),
            Linkshells = Unread(ChannelGroup.Linkshell),
            CrossWorld = Unread(ChannelGroup.CrossWorld),
            FreeCompany = Unread(ChannelGroup.FreeCompany),
        };

        var withUnread = new List<Conversation>();
        foreach (var conversation in byKey.Values)
        {
            if (conversation.Unread > 0) withUnread.Add(conversation);
        }
        withUnread.Sort((a, b) => b.LastActivity.CompareTo(a.LastActivity));

        foreach (var conversation in withUnread.Take(maxConversations))
        {
            snapshot.Conversations.Add(new StatusConversation
            {
                Group = (int)conversation.Group,
                Title = conversation.Title,
                World = conversation.WorldName,
                Unread = conversation.Unread,
                Sender = conversation.LastSender,
                Preview = conversation.LastPreview,
                At = conversation.LastActivity,
            });
        }

        return snapshot;
    }

    // ------------------------------------------------------------------
    // History paging
    // ------------------------------------------------------------------

    /// <summary>Reads the most recent page for a conversation the first time it is shown.</summary>
    public void EnsureHistory(Conversation conversation)
    {
        if (conversation.History != HistoryState.NotLoaded) return;
        if (!HasFile(conversation))
        {
            conversation.History = HistoryState.Loaded;
            conversation.ReachedStart = true;
            return;
        }
        LoadPage(conversation);
    }

    /// <returns>False when there is nothing older, or a page is already on its way.</returns>
    public bool LoadOlder(Conversation conversation)
    {
        if (!HasFile(conversation) || conversation.ReachedStart || conversation.History == HistoryState.Loading) return false;
        LoadPage(conversation);
        return true;
    }

    private void LoadPage(Conversation conversation)
    {
        conversation.History = HistoryState.Loading;
        var mine = generation;

        // Skip exactly the lines already in memory. Anything appended after
        // this call is queued behind the read, so it is not in the file the
        // read sees and must not be skipped either.
        history!.ReadTail($"{CharacterDir}/{conversation.File}", conversation.RawLinesLoaded, PageSize, page =>
            completed.Enqueue(() =>
            {
                if (mine != generation) return;

                conversation.History = HistoryState.Loaded;
                conversation.RawLinesLoaded += page.RawLines;
                conversation.ReachedStart = page.ReachedStart;
                if (page.Messages.Count == 0) return;

                conversation.Messages.InsertRange(0, page.Messages);
                conversation.PrependGeneration++;
            }));
    }

    // ------------------------------------------------------------------
    // Search
    // ------------------------------------------------------------------

    /// <summary>
    /// Looks for messages containing <paramref name="query"/> in the given
    /// conversations: through the whole saved history where there is one, and
    /// through what is in memory where there is not. <paramref name="done"/>
    /// gets the newest <paramref name="max"/> hits, on the thread that calls
    /// <see cref="Tick"/>, or straight away if nothing had to be read from disk.
    /// </summary>
    public void Search(IEnumerable<Conversation> scope, string query, int max, Action<List<SearchHit>> done)
    {
        var hits = new List<SearchHit>();
        var files = new List<(string Key, string File)>();
        foreach (var conversation in scope)
        {
            if (HasFile(conversation)) files.Add((conversation.Key, conversation.File));
            else MessageSearch.InMemory(conversation, query, max, hits);
        }

        if (files.Count == 0)
        {
            done(Newest(hits, max));
            return;
        }

        var mine = generation;
        history!.Search(CharacterDir!, files, query, max, found => completed.Enqueue(() =>
        {
            if (mine != generation) return;
            hits.AddRange(found);
            done(Newest(hits, max));
        }));
    }

    private static List<SearchHit> Newest(List<SearchHit> hits, int max)
    {
        hits.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        if (hits.Count > max) hits.RemoveRange(max, hits.Count - max);
        return hits;
    }

    // ------------------------------------------------------------------
    // Index
    // ------------------------------------------------------------------

    /// <summary>Writes the index now if there is anything to write. Called on logout and unload.</summary>
    public void FlushIndex()
    {
        indexDirtySince = 0;

        // Until the saved index has been read, what is in memory is only this
        // session's conversations. Writing that would erase all the others.
        if (!persist || !IndexLoaded || CharacterDir == null) return;

        var index = new HistoryIndex { Character = CharacterName, World = CharacterWorld };
        foreach (var conversation in byKey.Values)
        {
            if (conversation.Placeholder) continue;
            var worthKeeping = conversation.LastActivity > 0 || conversation.Pinned || conversation.Muted
                               || conversation.Closed || conversation.Draft.Length > 0;
            if (!worthKeeping) continue;

            index.Conversations.Add(new IndexEntry
            {
                Key = conversation.Key,
                Group = (int)conversation.Group,
                Title = conversation.Title,
                WorldId = conversation.WorldId,
                WorldName = conversation.WorldName,
                File = conversation.File,
                LastActivity = conversation.LastActivity,
                LastIncoming = conversation.LastIncoming,
                LastPreview = conversation.LastPreview,
                LastSender = conversation.LastSender,
                Unread = conversation.Unread,
                FirstUnread = conversation.Unread > 0 ? conversation.FirstUnreadTimestamp : 0,
                Pinned = conversation.Pinned,
                Muted = conversation.Muted,
                Closed = conversation.Closed,
                Draft = conversation.Draft,
            });
        }

        index.Conversations.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        history!.WriteIndex(CharacterDir, index);
    }

    /// <summary>
    /// When someone last said something in a saved conversation. An index
    /// written before this was kept does not say, but if the last message was
    /// someone else's, its time is the answer.
    /// </summary>
    private static long IncomingFrom(IndexEntry entry) =>
        entry.LastIncoming > 0 ? entry.LastIncoming : !string.IsNullOrEmpty(entry.LastSender) ? entry.LastActivity : 0;

    private void ApplyIndex(HistoryIndex index)
    {
        foreach (var entry in index.Conversations)
        {
            if (entry.Group is < 0 or >= ChannelGroups.Count) continue;
            var group = (ChannelGroup)entry.Group;

            if (byKey.TryGetValue(entry.Key, out var live))
            {
                // Already here because something arrived before the index had
                // been read. What it has is newer; the index fills in the rest.
                live.Pinned = entry.Pinned;
                live.Muted = entry.Muted;
                if (live.Draft.Length == 0) live.Draft = entry.Draft ?? string.Empty;
                if (live.LastActivity == 0)
                {
                    live.LastActivity = entry.LastActivity;
                    live.LastPreview = entry.LastPreview ?? string.Empty;
                    live.LastSender = entry.LastSender ?? string.Empty;
                }
                if (live.LastIncoming == 0) live.LastIncoming = IncomingFrom(entry);

                if (live.Muted)
                {
                    live.Unread = 0;
                    live.FirstUnreadTimestamp = 0;
                }
                else if (entry.Unread > 0)
                {
                    live.Unread += entry.Unread;
                    if (entry.FirstUnread > 0 && (live.FirstUnreadTimestamp == 0 || entry.FirstUnread < live.FirstUnreadTimestamp))
                        live.FirstUnreadTimestamp = entry.FirstUnread;
                }
                continue;
            }

            if (string.IsNullOrEmpty(entry.File) || string.IsNullOrEmpty(entry.Title)) continue;

            byKey[entry.Key] = new Conversation(entry.Key, group, entry.Title)
            {
                WorldId = entry.WorldId is >= 0 and <= ushort.MaxValue ? (ushort)entry.WorldId : (ushort)0,
                WorldName = entry.WorldName ?? string.Empty,
                File = entry.File,
                LastActivity = entry.LastActivity,
                LastIncoming = IncomingFrom(entry),
                LastPreview = entry.LastPreview ?? string.Empty,
                LastSender = entry.LastSender ?? string.Empty,
                Unread = entry.Muted ? 0 : Math.Max(0, entry.Unread),
                FirstUnreadTimestamp = entry.Muted ? 0 : entry.FirstUnread,
                Pinned = entry.Pinned,
                Muted = entry.Muted,
                Closed = entry.Closed,
                Draft = entry.Draft ?? string.Empty,
            };
        }
    }

    /// <summary>Adds a conversation for every history file that nothing in memory accounts for.</summary>
    private void ApplyScan(List<DiscoveredFile> files)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var conversation in byKey.Values) known.Add(conversation.File);

        var added = false;
        foreach (var file in files)
        {
            string key;
            ushort worldId = 0;
            if (file.Group == ChannelGroup.Tell)
            {
                worldId = ResolveWorld?.Invoke(file.World) ?? 0;
                // Without the world id the key would never match the one a
                // new message produces, and the player would appear twice.
                if (worldId == 0) continue;
                key = ConversationKey.ForTell(file.Name, worldId);
            }
            else
            {
                key = ConversationKey.ForLinkshell(file.Group, file.Name);
            }

            if (known.Contains(file.File) || byKey.ContainsKey(key)) continue;
            byKey[key] = new Conversation(key, file.Group, file.Name)
            {
                WorldId = worldId,
                WorldName = file.World,
                File = file.File,
                LastActivity = file.LastWriteMs,
            };
            added = true;
        }

        IndexLoaded = true;
        for (var i = 0; i < sortedDirty.Length; i++) sortedDirty[i] = true;
        if (added) MarkIndexDirty();
        Bump();
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private void Register(Conversation conversation)
    {
        byKey[conversation.Key] = conversation;
        sortedDirty[(int)conversation.Group] = true;
        Bump();
    }

    /// <summary>Moves what a slot-keyed placeholder collected into the linkshell it turned out to be.</summary>
    private void Merge(Conversation placeholder, Conversation target)
    {
        byKey.Remove(placeholder.Key);
        Rename(placeholder.Key, target.Key);

        foreach (var message in placeholder.Messages)
        {
            target.Messages.Add(message);
            if (HasFile(target))
            {
                history!.Append($"{CharacterDir}/{target.File}", message);
                target.RawLinesLoaded++;
            }
        }

        target.LastIncoming = Math.Max(target.LastIncoming, placeholder.LastIncoming);
        if (placeholder.LastActivity > target.LastActivity)
        {
            target.LastActivity = placeholder.LastActivity;
            target.LastPreview = placeholder.LastPreview;
            target.LastSender = placeholder.LastSender;
        }

        if (!target.Muted)
        {
            target.Unread += placeholder.Unread;
            if (target.FirstUnreadTimestamp == 0) target.FirstUnreadTimestamp = placeholder.FirstUnreadTimestamp;
        }

        if (target.Draft.Length == 0) target.Draft = placeholder.Draft;
        MarkIndexDirty();
    }

    /// <summary>Whether this conversation's messages are being written to, and can be paged from, disk.</summary>
    private bool HasFile(Conversation conversation) =>
        persist && history != null && CharacterDir != null && conversation.File.Length > 0;

    private bool IsViewed(Conversation conversation) => IsViewed(conversation.Key);

    /// <summary>Points every window that was showing one conversation at another, or at nothing.</summary>
    private void Rename(string from, string? to)
    {
        for (var i = 0; i < viewed.Length; i++)
        {
            if (viewed[i] != null && string.Equals(viewed[i], from, StringComparison.OrdinalIgnoreCase)) viewed[i] = to;
        }
    }

    private void MarkIndexDirty()
    {
        if (indexDirtySince == 0) indexDirtySince = clock();
    }

    private void Bump()
    {
        Revision++;
        unreadDirty = true;
        Changed?.Invoke();
    }

    private static string Preview(string text)
    {
        if (text.Length <= PreviewLength) return text;

        // Not in the middle of a surrogate pair.
        var cut = PreviewLength;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    private static int CompareTells(Conversation a, Conversation b)
    {
        if (a.Pinned != b.Pinned) return a.Pinned ? -1 : 1;
        var recent = b.LastActivity.CompareTo(a.LastActivity);
        return recent != 0 ? recent : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareLinkshells(Conversation a, Conversation b)
    {
        if (a.Pinned != b.Pinned) return a.Pinned ? -1 : 1;

        // Joined linkshells in slot order, as the game lists them; ones the
        // character has left go underneath, most recent first.
        var aJoined = a.Slot > 0;
        var bJoined = b.Slot > 0;
        if (aJoined != bJoined) return aJoined ? -1 : 1;
        if (aJoined) return a.Slot.CompareTo(b.Slot);

        var recent = b.LastActivity.CompareTo(a.LastActivity);
        return recent != 0 ? recent : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
    }
}
