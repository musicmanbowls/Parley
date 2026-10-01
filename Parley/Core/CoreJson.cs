using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Parley.Core.History;
using Parley.Core.Settings;

namespace Parley.Core;

/// <summary>
/// Source-generated serialisers for everything Parley writes to disk.
///
/// Generated rather than reflection-based so nothing is cached against this
/// assembly's types at runtime, which is what lets Dalamud unload the plugin
/// cleanly on a reload.
/// </summary>
[JsonSerializable(typeof(StoredMessage))]
[JsonSerializable(typeof(HistoryIndex))]
[JsonSerializable(typeof(Configuration))]
internal partial class CoreJsonContext : JsonSerializerContext;

internal static class CoreJson
{
    // The relaxed encoder leaves apostrophes and non-ASCII text readable. It
    // still escapes quotes, backslashes and control characters, which is what
    // keeps one message on one line.
    private static readonly JsonSerializerOptions LineOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    // Documents write every property. Omitting defaults here would drop a
    // false that overrides a true default, and read it back as true.
    private static readonly JsonSerializerOptions DocumentOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Compact, one object per line: history files.</summary>
    public static CoreJsonContext Lines { get; } = new(LineOptions);

    /// <summary>Indented and complete: the index and the configuration.</summary>
    public static CoreJsonContext Documents { get; } = new(DocumentOptions);
}
