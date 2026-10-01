using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Parley.Core;

namespace Parley.Game;

/// <summary>
/// What the links in a message lead to in the game: an item's details and
/// tooltip, the things the chat log's item menu can do, party finder listings,
/// and the game's numbered UI colours. Draw thread only, which in Dalamud is
/// the game's own thread.
/// </summary>
internal static unsafe class GameLinks
{
    private static readonly Dictionary<int, uint> Colours = [];
    private static object? tooltipOwner;
    private static uint tooltipItem;

    // ------------------------------------------------------------------
    // Items
    // ------------------------------------------------------------------

    public static ItemInfo? Item(uint rawId)
    {
        try
        {
            var id = ItemInfo.BaseId(rawId);
            if (ItemInfo.IsKeyItemId(rawId))
            {
                if (!Services.Data.GetExcelSheet<EventItem>().TryGetRow(id, out var key)) return null;
                var help = Services.Data.GetExcelSheet<EventItemHelp>().TryGetRow(id, out var row) ? row.Description.ExtractText() : string.Empty;
                return new ItemInfo(id, key.Name.ExtractText(), help, key.Icon, false, false, true, 1, 0, 0, "Key item", false, false, false);
            }

            if (!Services.Data.GetExcelSheet<Item>().TryGetRow(id, out var item)) return null;
            var category = item.ItemUICategory.IsValid ? item.ItemUICategory.Value.Name.ExtractText() : string.Empty;
            var search = item.ItemSearchCategory;
            return new ItemInfo(
                id,
                item.Name.ExtractText(),
                item.Description.ExtractText(),
                item.Icon,
                ItemInfo.IsHighQualityId(rawId),
                ItemInfo.IsCollectableId(rawId),
                false,
                item.Rarity,
                (int)item.LevelItem.RowId,
                item.LevelEquip,
                category,
                item.EquipSlotCategory.RowId != 0,
                search.IsValid && search.Value.Category == 3,
                !item.IsUntradable && search.RowId != 0);
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, $"Could not look up item {rawId}.");
            return null;
        }
    }

    public static ImTextureID? Icon(uint icon, bool highQuality)
    {
        if (icon == 0) return null;
        try
        {
            return Services.Textures.GetFromGameIcon(new GameIconLookup(icon, highQuality)).GetWrapOrDefault()?.Handle;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Shows the game's own item tooltip, the one its chat log shows, beside
    /// the cursor. The same approach Chat 2 uses. False if the game's tooltip
    /// window is not available, in which case the caller draws its own.
    /// </summary>
    /// <param name="owner">Whoever is showing it, so another window does not hide it by mistake.</param>
    public static bool ShowItemTooltip(uint rawId, object owner)
    {
        if (ReferenceEquals(owner, tooltipOwner) && tooltipItem == rawId) return true;

        try
        {
            var stage = AtkStage.Instance();
            var agent = AgentItemDetail.Instance();
            var addon = RaptureAtkModule.Instance()->RaptureAtkUnitManager.GetAddonByName("ItemDetail");
            if (stage == null || agent == null || addon == null || !addon->IsReady) return false;

            agent->DetailKind = ItemInfo.IsKeyItemId(rawId) ? DetailKind.KeyItem : DetailKind.Item;
            agent->TypeOrId = rawId;
            agent->Index = 0;
            agent->Flag1 &= 0xEF;
            agent->ItemId = rawId;
            agent->Flag2 = 1;
            agent->Flag3 = 0;
            agent->AddonId = addon->Id;

            // Without this the tooltip window decides it has nothing to show.
            stage->TooltipManager.TooltipType |= 2;
            addon->Show(false, 15);

            tooltipOwner = owner;
            tooltipItem = rawId;
            return true;
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, "Could not show the game's item tooltip.");
            return false;
        }
    }

    public static void HideItemTooltip(object owner)
    {
        if (!ReferenceEquals(owner, tooltipOwner)) return;
        tooltipOwner = null;
        tooltipItem = 0;

        try
        {
            // Hidden first, so closing it makes no sound.
            var addon = RaptureAtkModule.Instance()->RaptureAtkUnitManager.GetAddonByName("ItemDetail");
            if (addon != null) addon->Hide(true, false, 0);

            var agent = AgentItemDetail.Instance();
            if (agent == null) return;
            var result = stackalloc AtkValue[1];
            var values = stackalloc AtkValue[1];
            values->Type = AtkValueType.Int;
            values->Int = -1;
            agent->ReceiveEvent(result, values, 1, 1);
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, "Could not hide the game's item tooltip.");
        }
    }

    public static void TryOn(uint rawId) => Guard("try on an item", () => AgentTryon.TryOn(0xFF, rawId));

    public static void Compare(uint rawId) => Guard("compare an item", () => AgentItemComp.Instance()->CompareItem(0x4D, rawId, 0, 0));

    /// <summary>The game's "Search for Item": where in your inventories and retainers it is.</summary>
    public static void FindItem(uint rawId) => Guard("search for an item", () => ItemFinderModule.Instance()->SearchForItem(rawId));

    public static void SearchRecipes(uint itemId) => Guard("search recipes", () => AgentRecipeProductList.Instance()->SearchForRecipesUsingItem(itemId));

    public static void OpenPartyFinder(uint listingId) => Guard("open a party finder listing", () => AgentLookingForGroup.Instance()->OpenListing(listingId));

    // ------------------------------------------------------------------
    // Colours
    // ------------------------------------------------------------------

    /// <summary>A UI colour as 0xRRGGBBAA from the column for one of the game's themes, or 0 if there is no such colour.</summary>
    public static uint UiColour(ushort key, int theme)
    {
        var cacheKey = (key << 3) | (theme & 7);
        if (Colours.TryGetValue(cacheKey, out var known)) return known;

        uint value = 0;
        try
        {
            if (Services.Data.GetExcelSheet<UIColor>().TryGetRow(key, out var row))
            {
                value = theme switch
                {
                    1 => row.Light,
                    2 => row.ClassicFF,
                    3 => row.ClearBlue,
                    4 => row.ClearWhite,
                    5 => row.ClearGreen,
                    6 => row.Unknown2,
                    7 => row.Unknown3,
                    _ => row.Dark,
                };
            }
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, $"Could not read UI colour {key}.");
        }

        Colours[cacheKey] = value;
        return value;
    }

    private static void Guard(string what, System.Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, $"Could not {what}.");
        }
    }

    private static void Guard(string what, Func<bool> action) => Guard(what, () => { action(); });
}
