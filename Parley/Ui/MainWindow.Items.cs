using System.Numerics;
using Dalamud.Bindings.ImGui;
using Parley.Core;
using Parley.Core.Text;
using Parley.Core.Theme;
using Parley.Game;

namespace Parley.Ui;

/// <summary>
/// Items linked in messages: the tooltip on hover, the game's own where it can
/// be, and the menu a click opens, with the same things the game's chat log
/// offers for an item and a couple of web look-ups.
/// </summary>
internal sealed partial class MainWindow
{
    private const string ItemMenu = "##itemmenu";

    /// <summary>Rarity colours as the game draws item names.</summary>
    private static readonly Vector4[] RarityColours =
    [
        ColourMath.FromRgb(0xFFFFFF), ColourMath.FromRgb(0xFFFFFF), ColourMath.FromRgb(0x5FD38B),
        ColourMath.FromRgb(0x5C9EFF), ColourMath.FromRgb(0xB47AFF), ColourMath.FromRgb(0xFFFFFF),
        ColourMath.FromRgb(0xFFFFFF), ColourMath.FromRgb(0xF49AC2),
    ];

    private uint itemInfoFor;
    private ItemInfo? itemInfo;

    /// <summary>What there is to know about an item, looked up when the item changes and not every frame.</summary>
    private ItemInfo? Item(uint rawId)
    {
        if (rawId == itemInfoFor) return itemInfo;
        itemInfoFor = rawId;
        itemInfo = GameLinks.Item(rawId);
        return itemInfo;
    }

    private void HoverItem(TextLink link)
    {
        if (config.NativeItemTooltips && GameLinks.ShowItemTooltip(link.Id, this))
        {
            itemTooltipSeen = true;
            return;
        }

        if (Item(link.Id) is not { } info) return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, normalPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, normalSpacing);
        ImGui.BeginTooltip();
        try
        {
            DrawItemHeading(info, 40f * scale);
            if (info.Description.Length > 0)
            {
                ImGui.Separator();
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + (320f * scale));
                ImGui.TextUnformatted(info.Description);
                ImGui.PopTextWrapPos();
            }
            ImGui.TextDisabled("Click for more");
        }
        finally
        {
            ImGui.EndTooltip();
            ImGui.PopStyleVar(2);
        }
    }

    /// <summary>The icon, the name in its rarity's colour, and a line about what kind of item it is.</summary>
    private void DrawItemHeading(ItemInfo info, float iconSize)
    {
        if (GameLinks.Icon(info.Icon, info.HighQuality) is { } icon)
        {
            ImGui.Image(icon, new Vector2(iconSize, iconSize));
            ImGui.SameLine();
        }

        ImGui.BeginGroup();
        var name = info.HighQuality ? info.Name + " " : info.Collectable ? info.Name + " " : info.Name;
        var rarity = info.Rarity < RarityColours.Length ? RarityColours[info.Rarity] : palette.Text;
        ImGui.TextColored(ColourMath.LegibleOn(rarity, ImGui.GetStyle().Colors[(int)ImGuiCol.PopupBg] with { W = 1f }, palette.Text), name);

        var details = new List<string>(3);
        if (info.Category.Length > 0) details.Add(info.Category);
        if (info.ItemLevel > 1 && info.Equippable) details.Add($"Item level {info.ItemLevel}");
        if (info.EquipLevel > 1) details.Add($"Level {info.EquipLevel}");
        if (info.KeyItem) details.Add("Key item");
        if (details.Count > 0) ImGui.TextDisabled(string.Join(" · ", details));
        ImGui.EndGroup();
    }

    /// <summary>Called once at the end of the window's frame: puts away the game's tooltip if no item is under the cursor now.</summary>
    private void EndItemTooltip()
    {
        if (!itemTooltipSeen) GameLinks.HideItemTooltip(this);
        itemTooltipSeen = false;
    }

    private void DrawItemMenu(Conversation conversation)
    {
        if (!BeginMenu(ItemMenu)) return;
        try
        {
            if (menuLink is not { Kind: LinkKind.Item } link)
            {
                ImGui.CloseCurrentPopup();
                return;
            }

            var info = Item(link.Id);
            if (info == null)
            {
                ImGui.TextDisabled("This item is not in the game's data.");
                return;
            }

            DrawItemHeading(info, 32f * scale);
            ImGui.Separator();

            if (!info.KeyItem)
            {
                if (info.Equippable)
                {
                    if (ImGui.MenuItem("Try on")) GameLinks.TryOn(link.Id);
                    if (ImGui.MenuItem("Compare with what you have equipped")) GameLinks.Compare(link.Id);
                }

                if (ImGui.MenuItem("Find in your inventory")) GameLinks.FindItem(link.Id);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Searches your inventory, armoury chest, saddlebags and retainers, as the game's \"Search for Item\" does.");

                if (info.Material && ImGui.MenuItem("Recipes that use this")) GameLinks.SearchRecipes(info.Id);
            }

            var canReply = selected != null && selected.CanSend && store.HasCharacter;
            if (ImGui.MenuItem("Link in your reply", false, canReply)) InsertIntoDraft(conversation, plugin.ItemLinkToken(link.Id, info.Name) + " ");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Puts this item in your reply box. It is sent as a real item link the other person can hover and click.");

            if (ImGui.MenuItem("Copy item name")) ImGui.SetClipboardText(info.Name);

            if (!info.KeyItem)
            {
                ImGui.Separator();
                if (info.Marketable && ImGui.MenuItem("Market prices on Universalis")) plugin.OpenUrl($"https://universalis.app/market/{info.Id}");
                if (ImGui.MenuItem("Look up on Garland Tools")) plugin.OpenUrl($"https://www.garlandtools.org/db/#item/{info.Id}");
            }
        }
        finally
        {
            EndMenu();
        }
    }
}
