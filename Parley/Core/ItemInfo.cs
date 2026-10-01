namespace Parley.Core;

/// <summary>What the item menu and tooltip need to know about an item someone linked.</summary>
/// <param name="Id">The item's own id, without the high-quality or collectable offset.</param>
/// <param name="Rarity">1 common, 2 uncommon (green), 3 rare (blue), 4 relic (purple), 7 aetherial (pink).</param>
/// <param name="Equippable">Can be tried on or compared with what is equipped.</param>
/// <param name="Material">Used in crafting, so there are recipes to search for.</param>
/// <param name="Marketable">Sold on the market board, so prices can be looked up.</param>
public sealed record ItemInfo(
    uint Id,
    string Name,
    string Description,
    uint Icon,
    bool HighQuality,
    bool Collectable,
    bool KeyItem,
    byte Rarity,
    int ItemLevel,
    int EquipLevel,
    string Category,
    bool Equippable,
    bool Material,
    bool Marketable)
{
    /// <summary>The id the game puts in a link: high quality adds a million, collectable half a million, key items are their own range.</summary>
    public static uint BaseId(uint rawId) => rawId switch
    {
        >= 2_000_000 => rawId,
        >= 1_000_000 => rawId - 1_000_000,
        >= 500_000 => rawId - 500_000,
        _ => rawId,
    };

    public static bool IsKeyItemId(uint rawId) => rawId >= 2_000_000;
    public static bool IsHighQualityId(uint rawId) => rawId is >= 1_000_000 and < 2_000_000;
    public static bool IsCollectableId(uint rawId) => rawId is >= 500_000 and < 1_000_000;
}
