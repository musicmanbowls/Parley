using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Parley.Core;

namespace Parley.Game;

/// <summary>
/// Which linkshell is in which slot, for both kinds, and which free company the
/// character is in, read from the game's own lists. A slot with no name is one
/// the character has not filled, or one the game has not told the client about
/// yet, which is the case for the first moments after logging in. Framework
/// thread only.
/// </summary>
internal sealed unsafe class LinkshellDirectory
{
    private readonly string[] local = Empty();
    private readonly string[] cross = Empty();
    private readonly string[] company = [string.Empty];

    public IReadOnlyList<string> Local => local;
    public IReadOnlyList<string> Cross => cross;

    /// <summary>The free company as a list of one slot, like the linkshell lists. Empty name for none.</summary>
    public IReadOnlyList<string> FreeCompany => company;

    public string Name(ChannelGroup group, int slot)
    {
        if (slot < 1 || slot > group.Slots()) return string.Empty;
        return group switch
        {
            ChannelGroup.Linkshell => local[slot - 1],
            ChannelGroup.CrossWorld => cross[slot - 1],
            ChannelGroup.FreeCompany => company[0],
            _ => string.Empty,
        };
    }

    /// <returns>True if any slot now holds a different name.</returns>
    public bool Refresh()
    {
        var changed = false;

        var linkshells = InfoProxyLinkshell.Instance();
        for (var slot = 0; slot < ChannelGroups.SlotsPerGroup; slot++)
        {
            var name = string.Empty;
            if (linkshells != null)
            {
                var entry = linkshells->GetLinkshellInfo((uint)slot);
                if (entry != null && entry->Id != 0)
                {
                    var pointer = linkshells->GetLinkshellName(entry->Id);
                    if (pointer.HasValue) name = pointer.ToString();
                }
            }
            changed |= Set(local, slot, name);
        }

        var crossWorld = InfoProxyCrossWorldLinkshell.Instance();
        for (var slot = 0; slot < ChannelGroups.SlotsPerGroup; slot++)
        {
            var name = string.Empty;
            if (crossWorld != null)
            {
                var pointer = crossWorld->GetCrossworldLinkshellName((uint)slot);
                if (pointer != null && pointer->Length > 0) name = pointer->ToString();
            }
            changed |= Set(cross, slot, name);
        }

        // No name yet with an id present means the game has not filled it in;
        // the slot then waits under a placeholder like a linkshell's would.
        var freeCompany = InfoProxyFreeCompany.Instance();
        var companyName = freeCompany != null && freeCompany->Id != 0 ? freeCompany->NameString : string.Empty;
        changed |= Set(company, 0, companyName);

        return changed;
    }

    public void Clear()
    {
        for (var slot = 0; slot < ChannelGroups.SlotsPerGroup; slot++)
        {
            local[slot] = string.Empty;
            cross[slot] = string.Empty;
        }
        company[0] = string.Empty;
    }

    private static bool Set(string[] slots, int slot, string? name)
    {
        name ??= string.Empty;
        if (string.Equals(slots[slot], name, StringComparison.Ordinal)) return false;
        slots[slot] = name;
        return true;
    }

    private static string[] Empty()
    {
        var slots = new string[ChannelGroups.SlotsPerGroup];
        Array.Fill(slots, string.Empty);
        return slots;
    }
}
