using System.Numerics;
using static Parley.Core.Theme.ColourMath;

namespace Parley.Core.Theme;

/// <summary>
/// Builds a palette from Umbra's named theme colours, so the chat window
/// matches whatever colour profile the toolbar is using.
///
/// The names are the ones Umbra registers in its own UmbraColors. A profile
/// that lacks one falls back to the game-dark value for that slot rather than
/// failing, since profiles shared between users predate newer names.
/// </summary>
public static class UmbraThemeMapper
{
    public static Palette Map(IReadOnlyDictionary<string, uint> colours)
    {
        var fallback = GameThemes.For(GameThemes.Dark);
        var palette = fallback.Clone();

        Vector4 Read(string name, PaletteSlot otherwise) =>
            colours.TryGetValue(name, out var value) ? FromAbgr(value) : fallback[otherwise];

        var window = Read("Window.Background", PaletteSlot.WindowBg);
        var windowLight = Read("Window.BackgroundLight", PaletteSlot.HeaderBg);
        var titlebar = Read("Window.TitlebarBackground", PaletteSlot.TitleBg);
        var text = Read("Window.Text", PaletteSlot.Text);
        var accent = Read("Window.AccentColor", PaletteSlot.Accent) with { W = 1f };

        palette[PaletteSlot.WindowBg] = window;
        palette[PaletteSlot.SidebarBg] = Mix(window, titlebar, 0.5f);
        palette[PaletteSlot.HeaderBg] = windowLight;
        palette[PaletteSlot.InputBg] = Read("Input.Background", PaletteSlot.InputBg);
        palette[PaletteSlot.PopupBg] = Read("Widget.PopupBackground", PaletteSlot.PopupBg);
        palette[PaletteSlot.Border] = Read("Window.Border", PaletteSlot.Border);
        palette[PaletteSlot.TitleBg] = titlebar;
        palette[PaletteSlot.TitleBgActive] = Read("Window.TitlebarGradient1", PaletteSlot.TitleBgActive);
        palette[PaletteSlot.Text] = text;
        palette[PaletteSlot.TextMuted] = Read("Window.TextMuted", PaletteSlot.TextMuted);
        palette[PaletteSlot.Accent] = accent;
        palette[PaletteSlot.AccentText] = ReadableOn(accent);
        palette[PaletteSlot.RowHover] = WithAlpha(ReadableOn(window), 0.07f);
        palette[PaletteSlot.RowSelected] = WithAlpha(accent, 0.25f);
        palette[PaletteSlot.BubbleIn] = Shift(windowLight, 0.07f) with { W = 1f };
        palette[PaletteSlot.BubbleInText] = text with { W = 1f };

        var bubbleOut = Mix(accent, window with { W = 1f }, 0.35f);
        palette[PaletteSlot.BubbleOut] = bubbleOut;
        palette[PaletteSlot.BubbleOutText] = ReadableOn(bubbleOut);
        palette[PaletteSlot.Badge] = accent;
        palette[PaletteSlot.BadgeText] = ReadableOn(accent);
        palette[PaletteSlot.Notice] = palette[PaletteSlot.TextMuted];
        palette[PaletteSlot.Error] = IsDark(window) ? FromRgb(0xFF7A6B) : FromRgb(0xC0392B);

        var button = Read("Widget.Background", PaletteSlot.Button);
        var buttonHover = Read("Widget.BackgroundHover", PaletteSlot.ButtonHover);
        palette[PaletteSlot.Button] = button;
        palette[PaletteSlot.ButtonHover] = buttonHover;
        palette[PaletteSlot.ButtonActive] = Shift(buttonHover, 0.12f);
        palette[PaletteSlot.ScrollGrab] = Read("Window.ScrollbarThumb", PaletteSlot.ScrollGrab);
        return palette;
    }
}
