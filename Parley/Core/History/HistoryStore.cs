using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Parley.Core.History;

/// <summary>
/// Conversation history on disk: one JSON object per line, one file per
/// conversation, appended to and never rewritten in normal use.
///
/// Every operation runs on a single worker thread, in the order it was asked
/// for. That ordering is what paging relies on: a read queued after an append
/// always sees that append, so "the lines before the ones I already have" is
/// a stable question.
///
/// Files are opened for each operation and closed again, so nothing here
/// holds a handle that would stop someone moving or deleting their logs.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    /// <summary>A tail this large is a corrupt file, not a conversation.</summary>
    private const long MaxTailBytes = 16 * 1024 * 1024;

    private const int ScanBlock = 64 * 1024;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly BlockingCollection<Action> queue = [];
    private readonly Thread worker;
    private readonly Action<string, Exception>? onError;
    private bool disposed;

    public HistoryStore(string root, Action<string, Exception>? onError = null)
    {
        Root = Path.GetFullPath(root);
        this.onError = onError;
        worker = new Thread(Run) { IsBackground = true, Name = "Parley history" };
        worker.Start();
    }

    public string Root { get; }

    public void Append(string file, ChatMessage message)
    {
        // Serialised here rather than on the worker: the message is mutable
        // and belongs to the caller's thread.
        var line = JsonSerializer.Serialize(StoredMessage.From(message), CoreJson.Lines.StoredMessage);
        Enqueue("append history", () => AppendLine(Resolve(file), line));
    }

    /// <summary>
    /// Reads up to <paramref name="take"/> lines from the end of the file,
    /// ignoring the newest <paramref name="skip"/>. <paramref name="done"/> is
    /// called on the worker thread, always, even when the read fails.
    /// </summary>
    public void ReadTail(string file, int skip, int take, Action<HistoryPage> done)
    {
        if (!Enqueue("read history", () => done(Guard("read history", () => ReadTailCore(Resolve(file), skip, take), HistoryPage.Nothing))))
            done(HistoryPage.Nothing);
    }

    public Task<HistoryPage> ReadTail(string file, int skip, int take)
    {
        var source = new TaskCompletionSource<HistoryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        ReadTail(file, skip, take, source.SetResult);
        return source.Task;
    }

    public void WriteIndex(string directory, HistoryIndex index)
    {
        var json = JsonSerializer.Serialize(index, CoreJson.Documents.HistoryIndex);
        Enqueue("write index", () => WriteAtomic(Resolve(directory + "/index.json"), json));
    }

    /// <summary>Null when there is no index or it cannot be read; the caller then rebuilds from <see cref="Scan"/>.</summary>
    public void ReadIndex(string directory, Action<HistoryIndex?> done)
    {
        if (!Enqueue("read index", () => done(Guard("read index", () => ReadIndexCore(Resolve(directory + "/index.json")), null))))
            done(null);
    }

    /// <summary>
    /// Lists history files under a character directory, for rebuilding a lost
    /// index. The paths returned are relative to that directory.
    /// </summary>
    public void Scan(string directory, Action<List<DiscoveredFile>> done)
    {
        if (!Enqueue("scan history", () => done(Guard("scan history", () => ScanCore(Resolve(directory)), []))))
            done([]);
    }

    /// <summary>
    /// Reads whole history files looking for messages that contain the text.
    /// Runs behind any writes already queued, so it sees every message added
    /// before it was asked. <paramref name="done"/> gets at most
    /// <paramref name="max"/> hits, newest first, on the worker thread.
    /// </summary>
    /// <param name="files">Each conversation's key and its file, relative to <paramref name="directory"/>.</param>
    public void Search(string directory, IReadOnlyList<(string Key, string File)> files, string query, int max, Action<List<SearchHit>> done)
    {
        if (!Enqueue("search history", () => done(Guard("search history", () => SearchCore(directory, files, query, max), []))))
            done([]);
    }

    public void Delete(string file) => Enqueue("delete history", () =>
    {
        var path = Resolve(file);
        if (File.Exists(path)) File.Delete(path);
    });

    /// <summary>Removes every message older than <paramref name="cutoffMs"/> from every file in the directory.</summary>
    public void Prune(string directory, long cutoffMs, Action<int>? done = null)
    {
        if (!Enqueue("prune history", () => done?.Invoke(Guard("prune history", () => PruneCore(Resolve(directory), cutoffMs), 0))))
            done?.Invoke(0);
    }

    /// <summary>Blocks until everything queued so far has run. False if that took longer than the timeout.</summary>
    public bool Flush(int timeoutMs = 5000)
    {
        var done = new ManualResetEventSlim();
        if (!Enqueue("flush", done.Set)) return true;
        return done.Wait(timeoutMs);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        queue.CompleteAdding();
        // Whatever is queued is a message somebody sent or received; give it
        // a moment to reach the disk before the plugin goes away.
        worker.Join(3000);
    }

    private bool Enqueue(string what, Action work)
    {
        if (disposed || queue.IsAddingCompleted) return false;
        try
        {
            queue.Add(() =>
            {
                try { work(); }
                catch (Exception ex) { onError?.Invoke(what, ex); }
            });
            return true;
        }
        catch (InvalidOperationException)
        {
            // Lost the race with Dispose.
            return false;
        }
    }

    private T Guard<T>(string what, Func<T> work, T fallback)
    {
        try { return work(); }
        catch (Exception ex)
        {
            onError?.Invoke(what, ex);
            return fallback;
        }
    }

    private void Run()
    {
        foreach (var work in queue.GetConsumingEnumerable())
            work();
    }

    /// <summary>Maps a relative path into the root, refusing anything that would land outside it.</summary>
    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"'{relative}' is outside the history directory.");
        return full;
    }

    private static void AppendLine(string path, string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

        // A crash mid-write can leave a last line with no terminator. Close it
        // off, or this message would be glued on to the end of that one.
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            if (stream.ReadByte() != '\n') stream.WriteByte((byte)'\n');
        }

        stream.Seek(0, SeekOrigin.End);
        stream.Write(Utf8.GetBytes(line));
        stream.WriteByte((byte)'\n');
    }

    private static HistoryPage ReadTailCore(string path, int skip, int take)
    {
        if (take <= 0 || !File.Exists(path)) return HistoryPage.Nothing;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (length == 0) return HistoryPage.Nothing;

        var start = FindTailStart(stream, length, skip + take, out var reachedStart);
        if (length - start > MaxTailBytes) return HistoryPage.Nothing;

        var buffer = new byte[length - start];
        stream.Position = start;
        stream.ReadExactly(buffer);

        var lines = new List<(int Start, int Length)>(skip + take);
        var lineStart = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\n') continue;
            lines.Add((lineStart, i - lineStart));
            lineStart = i + 1;
        }
        if (lineStart < buffer.Length) lines.Add((lineStart, buffer.Length - lineStart));

        // The newest `skip` lines are the ones the caller already holds.
        var available = Math.Max(0, lines.Count - skip);
        var first = Math.Max(0, available - take);
        var messages = new List<ChatMessage>(available - first);
        for (var i = first; i < available; i++)
        {
            var span = buffer.AsSpan(lines[i].Start, lines[i].Length).TrimEnd((byte)'\r');
            if (span.IsEmpty) continue;
            try
            {
                var stored = JsonSerializer.Deserialize(span, CoreJson.Lines.StoredMessage);
                if (stored != null) messages.Add(stored.ToMessage());
            }
            catch (JsonException)
            {
                // One unreadable line is skipped rather than costing the page.
                // It still counts as a raw line, so paging stays aligned.
            }
        }

        return new HistoryPage(messages, available - first, reachedStart && first == 0);
    }

    /// <summary>
    /// Finds the offset at which the last <paramref name="wanted"/> lines begin
    /// by counting newlines backwards from the end, so reading a page costs the
    /// size of the page and not the size of the file.
    /// </summary>
    private static long FindTailStart(FileStream stream, long length, int wanted, out bool reachedStart)
    {
        var block = new byte[ScanBlock];
        var position = length;
        var found = 0;

        while (position > 0)
        {
            var count = (int)Math.Min(block.Length, position);
            position -= count;
            stream.Position = position;
            stream.ReadExactly(block, 0, count);

            for (var i = count - 1; i >= 0; i--)
            {
                if (block[i] != '\n') continue;
                var absolute = position + i;
                // The newline ending the final line terminates it; it does not start another.
                if (absolute == length - 1) continue;
                if (++found == wanted)
                {
                    reachedStart = false;
                    return absolute + 1;
                }
            }
        }

        reachedStart = true;
        return 0;
    }

    private static HistoryIndex? ReadIndexCore(string path)
    {
        if (!File.Exists(path)) return null;
        var index = JsonSerializer.Deserialize(File.ReadAllBytes(path), CoreJson.Documents.HistoryIndex);
        if (index == null) return null;
        index.Conversations ??= [];
        index.Conversations.RemoveAll(entry => entry == null || string.IsNullOrEmpty(entry.Key));
        return index;
    }

    private List<DiscoveredFile> ScanCore(string directory)
    {
        var found = new List<DiscoveredFile>();
        foreach (var group in ChannelGroups.All) Collect(group);
        return found;

        void Collect(ChannelGroup group)
        {
            var folder = ConversationKey.Folder(group);
            var path = Path.Combine(directory, folder);
            if (!Directory.Exists(path)) return;

            foreach (var file in Directory.EnumerateFiles(path, "*.jsonl"))
            {
                var info = new FileInfo(file);
                if (info.Length == 0) continue;

                var name = Path.GetFileNameWithoutExtension(file);
                // Undo the "~hash" that SafeFileName adds to names it had to alter.
                var tilde = name.LastIndexOf('~');
                if (tilde > 0 && name.Length - tilde == 9) name = name[..tilde];

                var world = string.Empty;
                if (group == ChannelGroup.Tell)
                {
                    var at = name.LastIndexOf('@');
                    if (at <= 0 || at == name.Length - 1) continue;
                    world = name[(at + 1)..];
                    name = name[..at];
                }

                // Relative to the character directory, the same shape Conversation.File has.
                var relative = $"{folder}/{Path.GetFileName(file)}";
                found.Add(new DiscoveredFile(group, relative, name, world,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds()));
            }
        }
    }

    private List<SearchHit> SearchCore(string directory, IReadOnlyList<(string Key, string File)> files, string query, int max)
    {
        var hits = new List<SearchHit>();
        if (query.Length == 0 || max <= 0) return hits;

        // The text is stored as JSON, which escapes some characters. Only a
        // query that JSON leaves alone can be looked for in the raw line first,
        // which skips parsing the great majority of lines.
        var quickTest = true;
        foreach (var ch in query)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch != ' ') quickTest = false;
        }

        foreach (var (key, file) in files)
        {
            var path = Resolve($"{directory}/{file}");
            if (!File.Exists(path)) continue;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Utf8);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0) continue;
                if (quickTest && line.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;

                StoredMessage? stored;
                try { stored = JsonSerializer.Deserialize(line, CoreJson.Lines.StoredMessage); }
                catch (JsonException) { continue; }

                if (stored?.M == null || (stored.F & (int)MessageFlags.Notice) != 0) continue;
                var at = MessageSearch.Find(stored.M, query);
                if (at < 0) continue;

                hits.Add(new SearchHit(key, stored.T, stored.S ?? string.Empty, stored.M, (stored.F & (int)MessageFlags.Outgoing) != 0, at, query.Length));

                // A common word can match most of a long history. Keep only
                // the newest few hundred as we go rather than all of them.
                if (hits.Count >= max * 4) Trim(hits, max);
            }
        }

        Trim(hits, max);
        return hits;

        static void Trim(List<SearchHit> list, int keep)
        {
            list.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            if (list.Count > keep) list.RemoveRange(keep, list.Count - keep);
        }
    }

    private static int PruneCore(string directory, long cutoffMs)
    {
        if (!Directory.Exists(directory)) return 0;

        var removed = 0;
        // Listed up front: files are rewritten and deleted inside the loop.
        foreach (var file in Directory.GetFiles(directory, "*.jsonl", SearchOption.AllDirectories))
        {
            var kept = new List<string>();
            var dropped = 0;
            var first = true;
            var untouched = false;

            foreach (var line in File.ReadLines(file, Utf8))
            {
                if (line.Length == 0) continue;
                var timestamp = TimestampOf(line);

                // Files are chronological. A first line that survives means
                // every line does, and the file need not be read further.
                if (first && timestamp >= cutoffMs)
                {
                    untouched = true;
                    break;
                }
                first = false;

                // A line with no readable timestamp is kept: pruning must not
                // be the thing that deletes something it could not understand.
                if (timestamp < cutoffMs && timestamp > 0) dropped++;
                else kept.Add(line);
            }

            if (untouched || dropped == 0) continue;

            removed += dropped;
            if (kept.Count == 0)
            {
                File.Delete(file);
                continue;
            }

            var builder = new StringBuilder();
            foreach (var line in kept) builder.Append(line).Append('\n');
            WriteAtomic(file, builder.ToString());
        }

        return removed;
    }

    private static long TimestampOf(string line)
    {
        try { return JsonSerializer.Deserialize(line, CoreJson.Lines.StoredMessage)?.T ?? 0; }
        catch (JsonException) { return 0; }
    }

    /// <summary>Writes beside the target and renames over it, so a crash leaves either the old file or the new one.</summary>
    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, Utf8);
        File.Move(temp, path, overwrite: true);
    }
}
