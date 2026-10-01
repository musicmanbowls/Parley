using System.Text.Json.Serialization;

namespace Parley.Ipc;

// This file is the whole contract between Parley and anything that wants to
// show its state, and it stands alone on purpose: the Umbra companion compiles
// its own copy rather than loading Parley's assembly, so nothing here may
// depend on another Parley type.

/// <summary>
/// Dalamud IPC gate names. Every gate is a function; all of them must be
/// called on the framework thread.
/// </summary>
public static class ParleyIpc
{
    /// <summary>Version of the status JSON. Bumped only for a breaking change.</summary>
    public const int Version = 1;

    /// <summary><c>Func&lt;int&gt;</c>: <see cref="Version"/>.</summary>
    public const string ApiVersion = "Parley.ApiVersion";

    /// <summary><c>Func&lt;string&gt;</c>: a <see cref="StatusSnapshot"/> as JSON.</summary>
    public const string GetStatus = "Parley.GetStatusV1";

    /// <summary><c>Func&lt;bool&gt;</c>: toggles the chat window; true if it ended up open.</summary>
    public const string ToggleWindow = "Parley.ToggleWindow";

    /// <summary><c>Func&lt;bool&gt;</c>: opens the chat window and brings it to the front.</summary>
    public const string OpenWindow = "Parley.OpenWindow";

    /// <summary><c>Func&lt;int&gt;</c>: marks everything read; returns how many messages that cleared.</summary>
    public const string MarkAllRead = "Parley.MarkAllRead";

    /// <summary><c>Func&lt;long&gt;</c>: token of the Umbra theme Parley last received, 0 for none.</summary>
    public const string GetThemeToken = "Parley.GetUmbraThemeToken";

    /// <summary><c>Func&lt;string, bool&gt;</c>: hands Parley an <see cref="UmbraTheme"/> as JSON.</summary>
    public const string SetTheme = "Parley.SetUmbraThemeV1";
}

public sealed class StatusSnapshot
{
    [JsonPropertyName("version")] public int Version { get; set; }

    /// <summary>Changes whenever anything else in the snapshot does.</summary>
    [JsonPropertyName("revision")] public long Revision { get; set; }

    [JsonPropertyName("loggedIn")] public bool LoggedIn { get; set; }

    [JsonPropertyName("windowOpen")] public bool WindowOpen { get; set; }

    /// <summary>Unread messages across all tells. Muted conversations never count.</summary>
    [JsonPropertyName("tells")] public int Tells { get; set; }

    [JsonPropertyName("linkshells")] public int Linkshells { get; set; }

    [JsonPropertyName("crossWorld")] public int CrossWorld { get; set; }

    /// <summary>Added after version 1 shipped; a reader that predates it sees none.</summary>
    [JsonPropertyName("freeCompany")] public int FreeCompany { get; set; }

    /// <summary>Conversations with unread messages, newest first, capped at a handful.</summary>
    [JsonPropertyName("conversations")] public List<StatusConversation> Conversations { get; set; } = [];
}

public sealed class StatusConversation
{
    /// <summary>0 tell, 1 linkshell, 2 cross-world linkshell, 3 free company.</summary>
    [JsonPropertyName("group")] public int Group { get; set; }

    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    /// <summary>Home world of the other player. Empty for linkshells.</summary>
    [JsonPropertyName("world")] public string World { get; set; } = string.Empty;

    [JsonPropertyName("unread")] public int Unread { get; set; }

    [JsonPropertyName("sender")] public string Sender { get; set; } = string.Empty;

    [JsonPropertyName("preview")] public string Preview { get; set; } = string.Empty;

    /// <summary>Unix milliseconds of the latest message.</summary>
    [JsonPropertyName("at")] public long At { get; set; }
}

public sealed class UmbraTheme
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    /// <summary>Chosen by the sender; Parley hands it back from <see cref="ParleyIpc.GetThemeToken"/>.</summary>
    [JsonPropertyName("token")] public long Token { get; set; }

    /// <summary>Umbra's named colours, each 0xAABBGGRR.</summary>
    [JsonPropertyName("colours")] public Dictionary<string, uint> Colours { get; set; } = [];
}

[JsonSerializable(typeof(StatusSnapshot))]
[JsonSerializable(typeof(UmbraTheme))]
internal partial class ParleyIpcJson : JsonSerializerContext;
