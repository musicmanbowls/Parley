using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core.Theme;

namespace Parley.Ui;

/// <summary>
/// Small drawing routines shared by the chat window's parts.
///
/// The window can sit open for hours, so the routines that run every frame
/// take care not to allocate: labels that repeat are built once and reused.
/// </summary>
internal static class Painter
{
    private const string Ellipsis = "…";
    private const int MaxBadge = 99;

    private static readonly string[] BadgeLabels = BuildBadgeLabels();
    private static readonly Dictionary<FontAwesomeIcon, string> IconStrings = [];

    /// <summary>A colour packed for the draw list, with the window's current fade applied.</summary>
    public static uint U32(Vector4 colour) => ImGui.GetColorU32(colour);

    /// <summary>The text, cut short with an ellipsis if it is wider than the space it has.</summary>
    public static string Fit(string text, float maxWidth)
    {
        if (maxWidth <= 0f || text.Length == 0) return string.Empty;
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;

        var ellipsisWidth = ImGui.CalcTextSize(Ellipsis).X;
        if (ellipsisWidth > maxWidth) return string.Empty;

        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text.AsSpan(0, middle)).X + ellipsisWidth <= maxWidth) low = middle;
            else high = middle - 1;
        }

        if (low > 0 && char.IsHighSurrogate(text[low - 1])) low--;
        return low <= 0 ? Ellipsis : string.Concat(text.AsSpan(0, low).TrimEnd(), Ellipsis);
    }

    public static string BadgeLabel(int count) => count > MaxBadge ? "99+" : BadgeLabels[Math.Max(count, 0)];

    public static float BadgeWidth(int count, float scale)
    {
        var height = ImGui.GetTextLineHeight() + (2f * scale);
        return MathF.Max(height, ImGui.CalcTextSize(BadgeLabel(count)).X + (10f * scale));
    }

    /// <summary>An unread count in a pill, with the middle of its right edge at the given point.</summary>
    public static void Badge(ImDrawListPtr drawList, Vector2 rightMiddle, int count, Palette palette, float scale)
    {
        var label = BadgeLabel(count);
        var textSize = ImGui.CalcTextSize(label);
        var height = ImGui.GetTextLineHeight() + (2f * scale);
        var width = MathF.Max(height, textSize.X + (10f * scale));
        var min = new Vector2(rightMiddle.X - width, rightMiddle.Y - (height * 0.5f));
        var max = new Vector2(rightMiddle.X, rightMiddle.Y + (height * 0.5f));

        drawList.AddRectFilled(min, max, U32(palette.Badge), height * 0.5f);
        drawList.AddText(new Vector2(min.X + ((width - textSize.X) * 0.5f), min.Y + ((height - textSize.Y) * 0.5f)), U32(palette.BadgeText), label);
    }

    /// <summary>A filled disc with a short label centred in it, standing in for a portrait.</summary>
    public static void Avatar(ImDrawListPtr drawList, Vector2 centre, float radius, string label, Vector4 fill)
    {
        drawList.AddCircleFilled(centre, radius, U32(fill), 24);
        var textSize = ImGui.CalcTextSize(label);
        drawList.AddText(centre - (textSize * 0.5f), U32(ColourMath.ReadableOn(fill)), label);
    }

    /// <summary>A FontAwesome glyph drawn at a point, in the icon font.</summary>
    public static void Icon(ImDrawListPtr drawList, Vector2 position, FontAwesomeIcon icon, Vector4 colour)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        drawList.AddText(position, U32(colour), IconString(icon));
        ImGui.PopFont();
    }

    public static Vector2 IconSize(FontAwesomeIcon icon)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        var size = ImGui.CalcTextSize(IconString(icon));
        ImGui.PopFont();
        return size;
    }

    /// <summary>
    /// A square, borderless button showing one icon: no background until the
    /// cursor is over it. Returns true on the frame it is clicked.
    /// </summary>
    public static bool IconButton(string id, FontAwesomeIcon icon, string tooltip, Palette palette, float size, Vector4? colour = null, bool enabled = true)
    {
        var position = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size)) && enabled;
        var hovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();

        if (hovered && enabled)
        {
            drawList.AddRectFilled(position, position + new Vector2(size, size),
                U32(ImGui.IsItemActive() ? palette.RowSelected : palette.RowHover), size * 0.25f);
        }

        var tint = colour ?? palette.TextMuted;
        if (!enabled) tint = ColourMath.WithAlpha(tint, tint.W * 0.4f);
        else if (hovered && colour == null) tint = palette.Text;

        var glyphSize = IconSize(icon);
        Icon(drawList, position + ((new Vector2(size, size) - glyphSize) * 0.5f), icon, tint);

        if (hovered && tooltip.Length > 0) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private static string IconString(FontAwesomeIcon icon)
    {
        if (!IconStrings.TryGetValue(icon, out var text))
        {
            text = icon.ToIconString();
            IconStrings[icon] = text;
        }
        return text;
    }

    private static string[] BuildBadgeLabels()
    {
        var labels = new string[MaxBadge + 1];
        for (var i = 0; i < labels.Length; i++) labels[i] = i.ToString();
        return labels;
    }
}

/// <summary>
/// Remembers the result of fitting one piece of text into one width, so the
/// measuring is done when the text or the space changes and not every frame.
/// </summary>
internal struct FitCache
{
    private string? source;
    private string? fitted;
    private float maxWidth;
    private float width;
    private int stamp;

    /// <param name="layoutStamp">Anything that changes when the font does.</param>
    /// <param name="textWidth">How wide the returned text is.</param>
    public string Get(string text, float available, int layoutStamp, out float textWidth)
    {
        if (fitted == null || !ReferenceEquals(text, source) || available != maxWidth || layoutStamp != stamp)
        {
            source = text;
            maxWidth = available;
            stamp = layoutStamp;
            fitted = Painter.Fit(text, available);
            width = fitted.Length > 0 ? ImGui.CalcTextSize(fitted).X : 0f;
        }

        textWidth = width;
        return fitted;
    }
}
