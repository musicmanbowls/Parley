namespace Parley.Core;

public enum HistoryState : byte
{
    /// <summary>Nothing has been read from disk yet; only this session's messages are in memory.</summary>
    NotLoaded,
    Loading,
    Loaded,
}

public sealed class Conversation
{
    public Conversation(string key, ChannelGroup group, string title)
    {
        Key = key;
        Group = group;
        Title = title;
    }

    public string Key { get; }
    public ChannelGroup Group { get; }

    /// <summary>Player name for a tell, linkshell name otherwise.</summary>
    public string Title;

    /// <summary>Home world of the other player. Tells only.</summary>
    public ushort WorldId;
    public string WorldName = string.Empty;

    /// <summary>Slot 1-8 while the character is in this linkshell, 0 otherwise. Always 0 for tells.</summary>
    public int Slot;

    /// <summary>
    /// Stands in for a linkshell slot whose name is not known yet. Never
    /// saved; folded into the real conversation as soon as the name arrives.
    /// </summary>
    public bool Placeholder;

    /// <summary>History path relative to the character directory.</summary>
    public string File = string.Empty;

    public readonly List<ChatMessage> Messages = [];

    public int Unread;

    /// <summary>Timestamp of the oldest unread message, which is where the "new" divider is drawn. 0 for none.</summary>
    public long FirstUnreadTimestamp;

    public long LastActivity;

    /// <summary>When someone other than the local player last said something here. 0 if never, as far as is known.</summary>
    public long LastIncoming;

    public string LastPreview = string.Empty;
    public string LastSender = string.Empty;

    public bool Pinned;

    /// <summary>Muted conversations still record messages but never count as unread.</summary>
    public bool Muted;

    /// <summary>Hidden from the sidebar. Reopens on the next message unless muted.</summary>
    public bool Closed;

    /// <summary>Text typed but not sent.</summary>
    public string Draft = string.Empty;

    public HistoryState History;

    /// <summary>
    /// How many lines at the tail of the history file are in <see cref="Messages"/>.
    /// Paging reads "the lines before these", so this has to count raw lines,
    /// including any that failed to parse.
    /// </summary>
    public int RawLinesLoaded;

    /// <summary>True once paging has reached the first line of the file.</summary>
    public bool ReachedStart;

    /// <summary>Bumped whenever older messages are inserted at the front, so a view can hold its scroll position.</summary>
    public int PrependGeneration;

    /// <summary>
    /// Whatever the view showing this conversation wants to keep between
    /// frames. Opaque here; the store never looks at it.
    /// </summary>
    public object? ViewCache;

    public bool IsTell => Group == ChannelGroup.Tell;

    /// <summary>Whether a message can be sent here right now, ignoring login state.</summary>
    public bool CanSend => IsTell || Slot > 0;

    /// <summary>"Alice Smith@Gilgamesh" for tells, the linkshell name otherwise.</summary>
    public string DisplayName => IsTell && WorldName.Length > 0 ? $"{Title}@{WorldName}" : Title;
}
