using System.Text.Json.Serialization;

namespace Parley.Core;

/// <summary>
/// The kinds of conversation Parley manages. The numbers are saved in history
/// indexes, so new kinds go on the end; the order the tabs appear in is
/// <see cref="ChannelGroups.All"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChannelGroup>))]
public enum ChannelGroup : byte
{
    Tell = 0,
    Linkshell = 1,
    CrossWorld = 2,
    FreeCompany = 3,
}

public static class ChannelGroups
{
    /// <summary>The game gives a character eight linkshell slots and eight cross-world ones.</summary>
    public const int SlotsPerGroup = 8;

    /// <summary>How many kinds there are, for arrays indexed by <see cref="ChannelGroup"/>.</summary>
    public const int Count = 4;

    /// <summary>Every kind, in the order the tabs show them.</summary>
    public static readonly ChannelGroup[] All = [ChannelGroup.Tell, ChannelGroup.FreeCompany, ChannelGroup.Linkshell, ChannelGroup.CrossWorld];

    public static string Label(this ChannelGroup group) => group switch
    {
        ChannelGroup.Tell => "Tells",
        ChannelGroup.Linkshell => "Linkshells",
        ChannelGroup.CrossWorld => "Cross-world",
        ChannelGroup.FreeCompany => "Free Company",
        _ => group.ToString(),
    };

    /// <summary>How many slots the game has for this kind. A character can only be in one free company.</summary>
    public static int Slots(this ChannelGroup group) => group switch
    {
        ChannelGroup.Linkshell or ChannelGroup.CrossWorld => SlotsPerGroup,
        ChannelGroup.FreeCompany => 1,
        _ => 0,
    };

    /// <summary>Text command that speaks in a slot, e.g. <c>/linkshell3</c>. Tells take a target instead.</summary>
    public static string SlotCommand(this ChannelGroup group, int slot) => group switch
    {
        ChannelGroup.Linkshell => $"/linkshell{slot}",
        ChannelGroup.CrossWorld => $"/cwlinkshell{slot}",
        ChannelGroup.FreeCompany => "/freecompany",
        _ => "/tell",
    };

    /// <summary>What the game calls a slot before its real name is known.</summary>
    public static string SlotLabel(this ChannelGroup group, int slot) => group switch
    {
        ChannelGroup.Linkshell => $"Linkshell {slot}",
        ChannelGroup.CrossWorld => $"Cross-world Linkshell {slot}",
        ChannelGroup.FreeCompany => "Free Company",
        _ => "Tell",
    };
}
