using System.Text.Json.Serialization;

namespace Parley.Core.History;

/// <summary>
/// One line of a history file. Property names are single letters because a
/// busy linkshell writes thousands of these, and defaults are omitted.
/// </summary>
public sealed class StoredMessage
{
    /// <summary>Unix milliseconds, UTC.</summary>
    [JsonPropertyName("t")] public long T { get; set; }

    /// <summary><see cref="MessageFlags"/> as a number.</summary>
    [JsonPropertyName("f")] public int F { get; set; }

    [JsonPropertyName("s")] public string? S { get; set; }

    [JsonPropertyName("w")] public int W { get; set; }

    [JsonPropertyName("m")] public string? M { get; set; }

    /// <summary>Encoded SeString as base64, present only for messages with links or icons.</summary>
    [JsonPropertyName("r")] public string? R { get; set; }

    public static StoredMessage From(ChatMessage message) => new()
    {
        T = message.Timestamp,
        F = (int)message.Flags,
        S = message.Sender.Length > 0 ? message.Sender : null,
        W = message.SenderWorld,
        M = message.Text,
        R = message.Rich is { Length: > 0 } rich ? Convert.ToBase64String(rich) : null,
    };

    public ChatMessage ToMessage()
    {
        byte[]? rich = null;
        if (!string.IsNullOrEmpty(R))
        {
            // A damaged rich payload costs the formatting, never the message.
            try { rich = Convert.FromBase64String(R); }
            catch (FormatException) { }
        }

        return new ChatMessage
        {
            Timestamp = T,
            Flags = (MessageFlags)(F & 0xFF),
            Sender = S ?? string.Empty,
            SenderWorld = W is >= 0 and <= ushort.MaxValue ? (ushort)W : (ushort)0,
            Text = M ?? string.Empty,
            Rich = rich,
        };
    }
}

/// <summary>Everything about a character's conversations except the messages themselves.</summary>
public sealed class HistoryIndex
{
    public int Version { get; set; } = 1;
    public string Character { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public List<IndexEntry> Conversations { get; set; } = [];
}

public sealed class IndexEntry
{
    public string Key { get; set; } = string.Empty;
    public int Group { get; set; }
    public string Title { get; set; } = string.Empty;
    public int WorldId { get; set; }
    public string WorldName { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public long LastActivity { get; set; }

    /// <summary>When someone other than the local player last said something. Missing from older indexes.</summary>
    public long LastIncoming { get; set; }

    public string LastPreview { get; set; } = string.Empty;
    public string LastSender { get; set; } = string.Empty;
    public int Unread { get; set; }
    public long FirstUnread { get; set; }
    public bool Pinned { get; set; }
    public bool Muted { get; set; }
    public bool Closed { get; set; }
    public string Draft { get; set; } = string.Empty;
}

/// <summary>A history file found on disk with no index entry describing it.</summary>
public sealed record DiscoveredFile(ChannelGroup Group, string File, string Name, string World, long LastWriteMs);

/// <summary>A run of messages read from the end of a history file, oldest first.</summary>
public sealed record HistoryPage(List<ChatMessage> Messages, int RawLines, bool ReachedStart)
{
    public static HistoryPage Nothing { get; } = new([], 0, true);
}
