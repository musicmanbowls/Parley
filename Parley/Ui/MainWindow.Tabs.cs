using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core;
using Parley.Core.Theme;

namespace Parley.Ui;

internal sealed partial class MainWindow
{
    private const string TabMenu = "##tabmenu";

    private ChannelGroup tabMenuFor;

    /// <summary>
    /// The strip across the top: one tab per kind of conversation, each with
    /// its unread count, and the window-wide actions on the right. A popped-out
    /// window has just its own kind there.
    /// </summary>
    private void DrawGroupTabs()
    {
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetFrameHeight() + (4f * scale);
        var rounding = 6f * scale;

        // Reserve the strip as one item, then place the tabs over it. Laying
        // them out by hand keeps their spacing independent of the theme.
        ImGui.Dummy(new Vector2(width, height));
        var after = ImGui.GetCursorScreenPos();

        // Short names when the full ones would run into the buttons on the right.
        var room = width - StripActionsWidth(height);
        var compact = TabsWidth(height, compact: false) > room;

        var x = start.X;
        if (GeneralAvailable) x = DrawGeneralTab(drawList, x, start.Y, height, rounding);

        foreach (var tab in ChannelGroups.All)
        {
            if (only is { } kind && tab != kind) continue;

            var label = compact ? tab.ShortLabel() : tab.Label();
            var popped = only == null && config.IsPoppedOut(tab);
            var unread = store.Unread(tab);
            var textSize = ImGui.CalcTextSize(label);
            var iconWidth = popped ? Painter.IconSize(FontAwesomeIcon.ExternalLinkAlt).X * 0.8f + (6f * scale) : 0f;
            var badgeWidth = unread > 0 ? Painter.BadgeWidth(unread, scale) + (6f * scale) : 0f;
            var tabWidth = textSize.X + iconWidth + badgeWidth + (26f * scale);

            ImGui.SetCursorScreenPos(new Vector2(x, start.Y));
            if (ImGui.InvisibleButton($"##tab{(int)tab}", new Vector2(tabWidth, height)) && only == null)
            {
                if (popped)
                {
                    // Its conversations are in a window of their own; bring that up.
                    plugin.ShowPopOut(tab);
                }
                else
                {
                    // Picking a tab picks the conversation it shows, the same as
                    // clicking that conversation would, keyboard included.
                    ShowGeneral(false);
                    SwitchGroup(tab);
                    if (searching) CloseSearch();
                    focusComposer = config.FocusInputOnOpen ? WhicheverIsShown : null;
                }
            }

            var hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && only == null)
            {
                tabMenuFor = tab;
                ImGui.OpenPopup(TabMenu);
            }
            if (hovered && popped) ImGui.SetTooltip($"{label} are in a window of their own.\nClick to bring it up; right-click to put them back here.");
            else if (hovered && tab == ChannelGroup.FreeCompany && FreeCompanyShown() is { } freeCompany) DrawBanner(freeCompany, FriendStatus.None);

            var active = tab == group && !popped && !ShowingGeneral;
            var min = new Vector2(x, start.Y);
            var max = new Vector2(x + tabWidth, start.Y + height);
            if (active) drawList.AddRectFilled(min, max, Painter.U32(palette.HeaderBg), rounding, ImDrawFlags.RoundCornersTop);
            else if (hovered) drawList.AddRectFilled(min, max, Painter.U32(palette.RowHover), rounding, ImDrawFlags.RoundCornersTop);

            var textX = x + (13f * scale);
            drawList.AddText(new Vector2(textX, start.Y + ((height - textSize.Y) * 0.5f)),
                Painter.U32(active || unread > 0 ? palette.Text : palette.TextMuted), label);

            if (popped)
            {
                ImGui.SetWindowFontScale(0.8f);
                var iconSize = Painter.IconSize(FontAwesomeIcon.ExternalLinkAlt);
                Painter.Icon(drawList, new Vector2(textX + textSize.X + (6f * scale), start.Y + ((height - iconSize.Y) * 0.5f)),
                    FontAwesomeIcon.ExternalLinkAlt, palette.TextMuted);
                ImGui.SetWindowFontScale(1f);
            }

            if (unread > 0)
                Painter.Badge(drawList, new Vector2(max.X - (9f * scale), start.Y + (height * 0.5f)), unread, palette, scale);

            if (active)
            {
                // The underline is in the colour the game's chat log gives this channel.
                drawList.AddRectFilled(
                    new Vector2(min.X + rounding, max.Y - (2f * scale)), new Vector2(max.X - rounding, max.Y),
                    Painter.U32(palette.Group(tab)), scale);
            }

            x += tabWidth + (2f * scale);
        }

        DrawTabMenu();
        DrawStripActions(start, width, height);

        drawList.AddLine(
            new Vector2(start.X, start.Y + height), new Vector2(start.X + width, start.Y + height),
            Painter.U32(palette.Border), 1f);

        ImGui.SetCursorScreenPos(after);
    }

    /// <summary>
    /// The free company the Free Company tab shows. With no list or tabs to
    /// point at when there is only the one, its banner shows on that tab.
    /// </summary>
    private Conversation? FreeCompanyShown()
    {
        if (selected is { Group: ChannelGroup.FreeCompany } shown) return shown;
        var list = store.InGroup(ChannelGroup.FreeCompany);
        return list.Count > 0 ? list[0] : null;
    }

    /// <summary>How wide the tabs come to, with their full names or short ones.</summary>
    private float TabsWidth(float height, bool compact)
    {
        var total = GeneralAvailable ? ImGui.CalcTextSize("General").X + (28f * scale) : 0f;
        foreach (var tab in ChannelGroups.All)
        {
            if (only is { } kind && tab != kind) continue;

            var unread = store.Unread(tab);
            total += ImGui.CalcTextSize(compact ? tab.ShortLabel() : tab.Label()).X + (28f * scale);
            if (unread > 0) total += Painter.BadgeWidth(unread, scale) + (6f * scale);
            if (only == null && config.IsPoppedOut(tab)) total += (Painter.IconSize(FontAwesomeIcon.ExternalLinkAlt).X * 0.8f) + (6f * scale);
        }
        return total;
    }

    /// <summary>How much of the strip the buttons on the right take, the same ones DrawStripActions draws.</summary>
    private float StripActionsWidth(float height)
    {
        var buttons = 2;
        if (store.HasCharacter)
        {
            buttons++;
            if (only == null || only == ChannelGroup.Tell) buttons++;
            if (only != null || (Shows(group) && !ShowingGeneral)) buttons++;
        }
        return (buttons * (height - (2f * scale))) + (8f * scale);
    }

    /// <summary>The General tab, first in the strip, for the game's own chat log. Returns where the next tab goes.</summary>
    private float DrawGeneralTab(ImDrawListPtr drawList, float x, float top, float height, float rounding)
    {
        const string label = "General";
        var textSize = ImGui.CalcTextSize(label);
        var tabWidth = textSize.X + (26f * scale);

        ImGui.SetCursorScreenPos(new Vector2(x, top));
        if (ImGui.InvisibleButton("##tabgeneral", new Vector2(tabWidth, height)))
        {
            ShowGeneral(true);
            if (searching) CloseSearch();
            focusComposer = config.FocusInputOnOpen ? WhicheverIsShown : null;
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered) ImGui.SetTooltip("The game's chat log, with your chat tabs");

        var active = ShowingGeneral;
        var min = new Vector2(x, top);
        var max = new Vector2(x + tabWidth, top + height);
        if (active) drawList.AddRectFilled(min, max, Painter.U32(palette.HeaderBg), rounding, ImDrawFlags.RoundCornersTop);
        else if (hovered) drawList.AddRectFilled(min, max, Painter.U32(palette.RowHover), rounding, ImDrawFlags.RoundCornersTop);

        drawList.AddText(new Vector2(x + (13f * scale), top + ((height - textSize.Y) * 0.5f)), Painter.U32(active ? palette.Text : palette.TextMuted), label);
        if (active)
            drawList.AddRectFilled(new Vector2(min.X + rounding, max.Y - (2f * scale)), new Vector2(max.X - rounding, max.Y), Painter.U32(palette.Accent), scale);

        return x + tabWidth + (2f * scale);
    }

    private void DrawTabMenu()
    {
        if (!BeginMenu(TabMenu)) return;
        try
        {
            var label = tabMenuFor.Label();
            if (config.IsPoppedOut(tabMenuFor))
            {
                if (ImGui.MenuItem($"Bring up the {label} window")) plugin.ShowPopOut(tabMenuFor);
                if (ImGui.MenuItem($"Put {label} back in this window")) plugin.DockBack(tabMenuFor);
            }
            else if (ImGui.MenuItem($"Pop {label} out into a window of its own"))
            {
                plugin.PopOut(tabMenuFor);
            }
        }
        finally
        {
            EndMenu();
        }
    }

    private void DrawStripActions(Vector2 start, float width, float height)
    {
        var size = height - (4f * scale);
        var x = start.X + width - size;
        var y = start.Y + (2f * scale);

        // Settings live here rather than in the title bar, which can be turned off.
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        if (Painter.IconButton("##settings", FontAwesomeIcon.Cog, "Parley settings", palette, size)) plugin.OpenSettings();
        x -= size + (2f * scale);

        ImGui.SetCursorScreenPos(new Vector2(x, y));
        var unread = only is { } kind ? store.Unread(kind) : store.TotalUnread;
        if (Painter.IconButton("##markall", FontAwesomeIcon.CheckDouble,
                unread > 0 ? only == null ? "Mark everything as read" : $"Mark all {only.Value.Label()} as read" : "Nothing unread",
                palette, size, enabled: unread > 0))
        {
            if (only is { } popped)
            {
                foreach (var conversation in store.InGroup(popped))
                {
                    store.MarkRead(conversation);
                    store.ClearUnreadMarker(conversation);
                }
            }
            else
            {
                store.MarkAllRead();
            }
        }

        if (!store.HasCharacter) return;

        x -= size + (2f * scale);
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        if (Painter.IconButton("##searchbutton", FontAwesomeIcon.Search, searching ? "Close the search" : "Search messages (Ctrl+F)", palette, size,
                searching ? ColourMath.LegibleOn(palette.Accent, palette.WindowBg, palette.Text) : null))
        {
            if (searching) CloseSearch();
            else OpenSearch();
        }

        if (only == null || only == ChannelGroup.Tell)
        {
            x -= size + (2f * scale);
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            if (Painter.IconButton("##newtell", FontAwesomeIcon.Plus, "New tell", palette, size))
                openNewTell = true;
        }

        x -= size + (2f * scale);
        ImGui.SetCursorScreenPos(new Vector2(x, y));
        if (only is { } own)
        {
            if (Painter.IconButton("##dock", FontAwesomeIcon.CompressAlt, $"Put {own.Label()} back in the main window", palette, size))
                plugin.DockBack(own);
        }
        else if (Shows(group) && !ShowingGeneral && Painter.IconButton("##popout", FontAwesomeIcon.ExternalLinkAlt, $"Pop {group.Label()} out into a window of its own", palette, size))
        {
            plugin.PopOut(group);
        }
    }
}
