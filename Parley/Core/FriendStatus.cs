namespace Parley.Core;

/// <summary>
/// What the friend list says about someone a tell is with. Default (not a
/// friend) when they are not on it, or the list has not been read yet.
/// </summary>
public readonly record struct FriendStatus(bool IsFriend, bool Online, bool Busy, bool Away, bool InDuty, ushort CurrentWorld, string CurrentWorldName)
{
    public static readonly FriendStatus None = default;

    /// <summary>Where they are, as the game's friend list shows it: the duty's name while in one, otherwise the zone. Empty if unknown.</summary>
    public string Place { get; init; } = string.Empty;

    /// <summary><see cref="Describe"/> with where they are on a second line, for tooltips.</summary>
    public string DescribeWithPlace(ushort homeWorld)
    {
        var state = Describe(homeWorld);
        return Online && Place.Length > 0 ? $"{state}\n{Place}" : state;
    }

    /// <summary>Online somewhere other than their home world.</summary>
    public bool Visiting(ushort homeWorld) => Online && CurrentWorld != 0 && CurrentWorld != homeWorld;

    /// <summary>A tell sent now is unlikely to be seen: they are offline, have set themselves busy, or are in a duty.</summary>
    public bool Unreachable => IsFriend && (!Online || Busy || InDuty);

    /// <summary>A few words for under a name: "online", "busy", "in a duty", "offline".</summary>
    public string Describe(ushort homeWorld)
    {
        if (!IsFriend) return string.Empty;
        if (!Online) return "Offline";

        var state = Busy ? "Busy" : InDuty ? "In a duty" : Away ? "Away" : "Online";
        return Visiting(homeWorld) && CurrentWorldName.Length > 0 ? $"{state} on {CurrentWorldName}" : state;
    }
}
