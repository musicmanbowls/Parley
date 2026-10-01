namespace Parley.Core;

/// <summary>
/// Friends' statuses as last read from the game, kept steady while the game
/// fetches its friend list again.
///
/// A fetch empties the game's copy of the list and fills it back in as the
/// server's answer arrives, which takes a moment. Read during that moment, the
/// list is short or empty, and taking it at its word would make everyone's
/// status vanish and come back. So someone missing from one reading keeps the
/// status last read for them, and is only dropped once they have been missing
/// for longer than any fetch takes: then they really are off the list.
/// </summary>
public sealed class FriendStatusBook
{
    private readonly Dictionary<(string Name, ushort World), Entry> entries = [];
    private readonly List<(string, ushort)> stale = [];

    /// <summary>How long someone can be missing from the list before they are taken to be no longer on it.</summary>
    public long KeepMissingMs { get; init; } = 20_000;

    public int Count => entries.Count;

    /// <summary>Takes in one reading of the game's list, made at <paramref name="now"/> (milliseconds on any steady clock).</summary>
    public void Update(IEnumerable<(string Name, ushort World, FriendStatus Status)> reading, long now)
    {
        foreach (var (name, world, status) in reading) entries[(name.ToLowerInvariant(), world)] = new Entry(status, now);

        stale.Clear();
        foreach (var (key, entry) in entries)
        {
            if (now - entry.Seen > KeepMissingMs) stale.Add(key);
        }
        foreach (var key in stale) entries.Remove(key);
    }

    public FriendStatus Get(string name, ushort world) =>
        entries.TryGetValue((name.ToLowerInvariant(), world), out var entry) ? entry.Status : FriendStatus.None;

    public void Clear() => entries.Clear();

    private readonly record struct Entry(FriendStatus Status, long Seen);
}
