using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core;
using Parley.Core.Text;

namespace Parley.Ui;

/// <summary>
/// The symbol button beside the reply box: hearts, stars, arrows, numbers and
/// the game's own icons, each one a character the game's font has, so the
/// person on the other end sees it too. A click puts it in the reply where the
/// caret was; Shift+click keeps the menu open to add more.
/// </summary>
internal sealed partial class MainWindow
{
    private const string SymbolMenu = "##symbols";

    private int symbolGroup;

    /// <summary>The symbols of each group that the window's font can draw, worked out once per font.</summary>
    private Symbols.Symbol[][]? drawableSymbols;
    private int drawableStamp = -1;

    private void DrawSymbolButton(Conversation conversation, bool enabled, float size)
    {
        if (Painter.IconButton("##symbolbutton", FontAwesomeIcon.Heart, enabled ? "Symbols" : string.Empty, palette, size, enabled: enabled))
            ImGui.OpenPopup(SymbolMenu);

        if (!BeginMenu(SymbolMenu)) return;
        try
        {
            DrawSymbolPicker(conversation);
        }
        finally
        {
            EndMenu();
        }
    }

    private void DrawSymbolPicker(Conversation conversation)
    {
        var groups = DrawableSymbols();
        var cell = ImGui.GetFrameHeight() + (6f * scale);
        var columns = 10;

        // Recently used first, when there are any.
        var recent = config.RecentSymbols;
        if (recent.Count > 0)
        {
            ImGui.TextDisabled("Recent");
            for (var i = 0; i < recent.Count; i++)
            {
                if (i % columns != 0) ImGui.SameLine();
                if (SymbolCell($"##recent{i}", recent[i], string.Empty, cell)) Pick(conversation, recent[i]);
            }
            ImGui.Separator();
        }

        for (var g = 0; g < Symbols.Groups.Length; g++)
        {
            if (g > 0) ImGui.SameLine();
            if (ImGui.Selectable(Symbols.Groups[g].Name, symbolGroup == g, ImGuiSelectableFlags.DontClosePopups, ImGui.CalcTextSize(Symbols.Groups[g].Name)))
                symbolGroup = g;
        }
        ImGui.Separator();

        var items = groups[Math.Clamp(symbolGroup, 0, groups.Length - 1)];
        for (var i = 0; i < items.Length; i++)
        {
            if (i % columns != 0) ImGui.SameLine();
            if (SymbolCell($"##symbol{i}", items[i].Text, items[i].Name, cell)) Pick(conversation, items[i].Text);
        }

        if (items.Length == 0) ImGui.TextDisabled("The font in use has none of these.");
        ImGui.TextDisabled("Shift+click to add several.");
    }

    /// <summary>One square in the grid. True when clicked.</summary>
    private bool SymbolCell(string id, string symbol, string name, float size)
    {
        var position = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();
        if (hovered) drawList.AddRectFilled(position, position + new Vector2(size, size), Painter.U32(palette.RowHover), 4f * scale);

        var glyph = ImGui.CalcTextSize(symbol);
        drawList.AddText(position + ((new Vector2(size, size) - glyph) * 0.5f), Painter.U32(palette.Text), symbol);
        if (hovered && name.Length > 0) ImGui.SetTooltip(name);
        return clicked;
    }

    private void Pick(Conversation conversation, string symbol)
    {
        InsertIntoDraft(conversation, symbol);
        config.UsedSymbol(symbol);
        plugin.SaveConfig();
        if (!ImGui.GetIO().KeyShift) ImGui.CloseCurrentPopup();
    }

    /// <summary>
    /// Leaves out anything the window's font cannot draw. Dalamud's default
    /// font has every character the game's does, but a font picked in its
    /// settings may not.
    /// </summary>
    private Symbols.Symbol[][] DrawableSymbols()
    {
        if (drawableSymbols != null && drawableStamp == layoutStamp) return drawableSymbols;

        var font = ImGui.GetFont();
        var groups = new Symbols.Symbol[Symbols.Groups.Length][];
        for (var g = 0; g < groups.Length; g++)
        {
            var list = new List<Symbols.Symbol>();
            foreach (var symbol in Symbols.Groups[g].Items)
            {
                if (HasGlyph(font, symbol.Text)) list.Add(symbol);
            }
            groups[g] = [.. list];
        }

        drawableSymbols = groups;
        drawableStamp = layoutStamp;
        return groups;
    }

    private static unsafe bool HasGlyph(ImFontPtr font, string text)
    {
        foreach (var ch in text)
        {
            if (font.Handle->FindGlyphNoFallback(ch) == null) return false;
        }
        return true;
    }
}
