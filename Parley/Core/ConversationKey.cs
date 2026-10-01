using System.Text;

namespace Parley.Core;

/// <summary>
/// Identity and file naming for conversations.
///
/// Tells are keyed by name and home world id. Linkshells are keyed by their
/// name rather than their slot: slots can be reordered in game and differ per
/// character, and keying on them would splice two linkshells' histories
/// together the first time that happened.
/// </summary>
public static class ConversationKey
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private const int MaxFileNameLength = 80;

    public static string ForTell(string name, ushort worldId) => $"t:{name}@{worldId}";

    public static string ForLinkshell(ChannelGroup group, string name) => $"{Prefix(group)}:{name}";

    /// <summary>
    /// Fallback for a slot whose name the game has not supplied yet. The
    /// separator is a control character so it can never equal a real name.
    /// </summary>
    public static string ForSlot(ChannelGroup group, int slot) => $"{Prefix(group)}:\u001f{slot}";

    private static char Prefix(ChannelGroup group) => group switch
    {
        ChannelGroup.Tell => 't',
        ChannelGroup.Linkshell => 'l',
        ChannelGroup.FreeCompany => 'f',
        _ => 'c',
    };

    /// <summary>The folder each kind of conversation's history is kept in, under the character's directory.</summary>
    public static string Folder(ChannelGroup group) => group switch
    {
        ChannelGroup.Tell => "tells",
        ChannelGroup.Linkshell => "linkshells",
        ChannelGroup.FreeCompany => "freecompany",
        _ => "crossworld",
    };

    /// <summary>History path relative to the character's directory, always with forward slashes.</summary>
    public static string FileFor(ChannelGroup group, string title, string worldName)
    {
        var name = group == ChannelGroup.Tell && worldName.Length > 0 ? $"{title}@{worldName}" : title;
        return $"{Folder(group)}/{SafeFileName(name)}.jsonl";
    }

    /// <summary>
    /// A name that is legal on Windows and on the filesystems Wine maps to.
    /// Anything that had to be altered gets a short hash of the original, so
    /// two linkshells that differ only in a stripped character stay apart.
    /// </summary>
    public static string SafeFileName(string name)
    {
        var builder = new StringBuilder(name.Length);
        var changed = false;
        foreach (var ch in name)
        {
            if (ch < 32 || ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            {
                builder.Append('_');
                changed = true;
            }
            else
            {
                builder.Append(ch);
            }
        }

        var safe = builder.ToString().TrimEnd(' ', '.');
        if (safe.Length != builder.Length) changed = true;

        if (safe.Length > MaxFileNameLength)
        {
            safe = safe[..MaxFileNameLength].TrimEnd(' ', '.');
            changed = true;
        }

        if (safe.Length == 0)
        {
            safe = "_";
            changed = true;
        }

        var stem = safe.Split('.')[0];
        if (ReservedNames.Contains(stem))
        {
            safe = "_" + safe;
            changed = true;
        }

        return changed ? $"{safe}~{Fnv1a(name):x8}" : safe;
    }

    private static uint Fnv1a(string text)
    {
        var hash = 2166136261u;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 16777619u;
        }
        return hash;
    }
}
