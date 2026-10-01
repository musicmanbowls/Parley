using System.Numerics;
using static Parley.Core.Theme.ColourMath;

namespace Parley.Core.Theme;

/// <summary>
/// Palettes modelled on the eight UI themes in the game's System Configuration.
///
/// These are approximations. The game does not expose its theme colours as
/// data, so there is nothing to read them from; only the choice of theme
/// (ColorThemeType) is read. The colours for each were taken from that
/// theme's window textures (ui/uld/img01 to img07 hold themes 1 to 7) and
/// adjusted to sit comfortably beside the game's own windows, rather than to
/// match them exactly.
/// </summary>
public static class GameThemes
{
    public const int Dark = 0;
    public const int Light = 1;
    public const int ClassicFf = 2;
    public const int ClearBlue = 3;
    public const int ClearWhite = 4;
    public const int ClearGreen = 5;
    public const int ClearGrey = 6;
    public const int ClearPink = 7;

    /// <summary>How many themes there are, numbered from 0 in the order System Configuration lists them.</summary>
    public const int Count = 8;

    // The game's default chat log colours, used until the player's own are read.
    public static readonly Vector4 DefaultTell = FromRgb(0xFFB8DE);
    public static readonly Vector4 DefaultLinkshell = FromRgb(0xD4FF7D);
    public static readonly Vector4 DefaultCrossWorld = FromRgb(0xD4FF7D);
    public static readonly Vector4 DefaultFreeCompany = FromRgb(0xABDBE5);

    public static bool IsKnown(int theme) => theme is >= 0 and < Count;

    public static string Name(int theme) => theme switch
    {
        Light => "Light",
        ClassicFf => "Classic FF",
        ClearBlue => "Clear Blue",
        ClearWhite => "Clear White",
        ClearGreen => "Clear Green",
        ClearGrey => "Clear Grey",
        ClearPink => "Clear Pink",
        _ => "Dark",
    };

    public static Palette For(int theme) => theme switch
    {
        Light => BuildLight(),
        ClassicFf => BuildClassic(),
        ClearBlue => BuildClearBlue(),
        ClearWhite => BuildClearWhite(),
        ClearGreen => BuildClearGreen(),
        ClearGrey => BuildClearGrey(),
        ClearPink => BuildClearPink(),
        _ => BuildDark(),
    };

    private static Palette BuildDark()
    {
        var accent = FromRgb(0xD4B06A);
        var p = new Palette
        {
            [PaletteSlot.WindowBg] = FromRgb(0x2A2A2A, 0.97f),
            [PaletteSlot.SidebarBg] = FromRgb(0x212121, 0.97f),
            [PaletteSlot.HeaderBg] = FromRgb(0x343434),
            [PaletteSlot.InputBg] = FromRgb(0x1B1B1B),
            [PaletteSlot.PopupBg] = FromRgb(0x2A2A2A, 0.98f),
            [PaletteSlot.Border] = FromRgb(0x5A5346),
            [PaletteSlot.TitleBg] = FromRgb(0x1E1E1E),
            [PaletteSlot.TitleBgActive] = FromRgb(0x3A352B),
            [PaletteSlot.Text] = FromRgb(0xEEEEEE),
            [PaletteSlot.TextMuted] = FromRgb(0xA09C94),
            [PaletteSlot.Accent] = accent,
            [PaletteSlot.AccentText] = FromRgb(0x2A2118),
            [PaletteSlot.RowHover] = new Vector4(1f, 1f, 1f, 0.06f),
            [PaletteSlot.RowSelected] = WithAlpha(accent, 0.22f),
            [PaletteSlot.BubbleIn] = FromRgb(0x3E3E3E),
            [PaletteSlot.BubbleInText] = FromRgb(0xF0F0F0),
            [PaletteSlot.BubbleOut] = FromRgb(0x6B5730),
            [PaletteSlot.BubbleOutText] = FromRgb(0xFFF6DF),
            [PaletteSlot.Badge] = FromRgb(0xE0533D),
            [PaletteSlot.BadgeText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Notice] = FromRgb(0xA09C94),
            [PaletteSlot.Error] = FromRgb(0xFF7A6B),
            [PaletteSlot.Button] = FromRgb(0x4A4A4A),
            [PaletteSlot.ButtonHover] = FromRgb(0x5C5C5C),
            [PaletteSlot.ButtonActive] = FromRgb(0x6E6E6E),
            [PaletteSlot.ScrollGrab] = FromRgb(0x6A6A6A),
        };
        return WithDefaultGroups(p);
    }

    private static Palette BuildLight()
    {
        var accent = FromRgb(0x9A7440);
        var p = new Palette
        {
            [PaletteSlot.WindowBg] = FromRgb(0xECE7DB, 0.98f),
            [PaletteSlot.SidebarBg] = FromRgb(0xE0DACB, 0.98f),
            [PaletteSlot.HeaderBg] = FromRgb(0xD8D1C0),
            [PaletteSlot.InputBg] = FromRgb(0xF8F5EE),
            [PaletteSlot.PopupBg] = FromRgb(0xF2EEE4, 0.99f),
            [PaletteSlot.Border] = FromRgb(0xB5A98F),
            [PaletteSlot.TitleBg] = FromRgb(0xD8D1C0),
            [PaletteSlot.TitleBgActive] = FromRgb(0xC9BFA8),
            [PaletteSlot.Text] = FromRgb(0x2F2A24),
            [PaletteSlot.TextMuted] = FromRgb(0x7A7164),
            [PaletteSlot.Accent] = accent,
            [PaletteSlot.AccentText] = FromRgb(0xFFFFFF),
            [PaletteSlot.RowHover] = new Vector4(0f, 0f, 0f, 0.06f),
            [PaletteSlot.RowSelected] = WithAlpha(accent, 0.20f),
            [PaletteSlot.BubbleIn] = FromRgb(0xFFFFFF),
            [PaletteSlot.BubbleInText] = FromRgb(0x2F2A24),
            [PaletteSlot.BubbleOut] = accent,
            [PaletteSlot.BubbleOutText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Badge] = FromRgb(0xD9483B),
            [PaletteSlot.BadgeText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Notice] = FromRgb(0x7A7164),
            [PaletteSlot.Error] = FromRgb(0xC0392B),
            [PaletteSlot.Button] = FromRgb(0xD2C9B4),
            [PaletteSlot.ButtonHover] = FromRgb(0xC6BBA2),
            [PaletteSlot.ButtonActive] = FromRgb(0xB8AB8E),
            [PaletteSlot.ScrollGrab] = FromRgb(0xB0A58E),
        };
        return WithDefaultGroups(p);
    }

    private static Palette BuildClassic()
    {
        var accent = FromRgb(0x7FB2FF);
        var p = new Palette
        {
            [PaletteSlot.WindowBg] = FromRgb(0x10205A, 0.97f),
            [PaletteSlot.SidebarBg] = FromRgb(0x0B1848, 0.97f),
            [PaletteSlot.HeaderBg] = FromRgb(0x1A2F7A),
            [PaletteSlot.InputBg] = FromRgb(0x081238),
            [PaletteSlot.PopupBg] = FromRgb(0x10205A, 0.98f),
            [PaletteSlot.Border] = FromRgb(0xB8C0E0),
            [PaletteSlot.TitleBg] = FromRgb(0x0B1848),
            [PaletteSlot.TitleBgActive] = FromRgb(0x1A2F7A),
            [PaletteSlot.Text] = FromRgb(0xFFFFFF),
            [PaletteSlot.TextMuted] = FromRgb(0xA9B4DA),
            [PaletteSlot.Accent] = accent,
            [PaletteSlot.AccentText] = FromRgb(0x06123A),
            [PaletteSlot.RowHover] = new Vector4(1f, 1f, 1f, 0.08f),
            [PaletteSlot.RowSelected] = WithAlpha(accent, 0.25f),
            [PaletteSlot.BubbleIn] = FromRgb(0x22378A),
            [PaletteSlot.BubbleInText] = FromRgb(0xFFFFFF),
            [PaletteSlot.BubbleOut] = FromRgb(0x3D6AD6),
            [PaletteSlot.BubbleOutText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Badge] = FromRgb(0xFF6B5E),
            [PaletteSlot.BadgeText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Notice] = FromRgb(0xA9B4DA),
            [PaletteSlot.Error] = FromRgb(0xFF8A80),
            [PaletteSlot.Button] = FromRgb(0x24409A),
            [PaletteSlot.ButtonHover] = FromRgb(0x2E50B8),
            [PaletteSlot.ButtonActive] = FromRgb(0x3A60D0),
            [PaletteSlot.ScrollGrab] = FromRgb(0x4A64B8),
        };
        return WithDefaultGroups(p);
    }

    private static Palette BuildClearBlue()
    {
        var accent = FromRgb(0x5CC8E8);
        var p = new Palette
        {
            [PaletteSlot.WindowBg] = FromRgb(0x16324A, 0.92f),
            [PaletteSlot.SidebarBg] = FromRgb(0x102739, 0.93f),
            [PaletteSlot.HeaderBg] = FromRgb(0x1E4463, 0.95f),
            [PaletteSlot.InputBg] = FromRgb(0x0C1E2D),
            [PaletteSlot.PopupBg] = FromRgb(0x16324A, 0.97f),
            [PaletteSlot.Border] = FromRgb(0x5FA8C8),
            [PaletteSlot.TitleBg] = FromRgb(0x102739),
            [PaletteSlot.TitleBgActive] = FromRgb(0x1E4463),
            [PaletteSlot.Text] = FromRgb(0xF2FAFF),
            [PaletteSlot.TextMuted] = FromRgb(0x9CC3D6),
            [PaletteSlot.Accent] = accent,
            [PaletteSlot.AccentText] = FromRgb(0x062230),
            [PaletteSlot.RowHover] = new Vector4(1f, 1f, 1f, 0.07f),
            [PaletteSlot.RowSelected] = WithAlpha(accent, 0.24f),
            [PaletteSlot.BubbleIn] = FromRgb(0x21506F),
            [PaletteSlot.BubbleInText] = FromRgb(0xF2FAFF),
            [PaletteSlot.BubbleOut] = FromRgb(0x2F9CC0),
            [PaletteSlot.BubbleOutText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Badge] = FromRgb(0xFF7A59),
            [PaletteSlot.BadgeText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Notice] = FromRgb(0x9CC3D6),
            [PaletteSlot.Error] = FromRgb(0xFF9080),
            [PaletteSlot.Button] = FromRgb(0x24597C),
            [PaletteSlot.ButtonHover] = FromRgb(0x2D6E98),
            [PaletteSlot.ButtonActive] = FromRgb(0x3784B4),
            [PaletteSlot.ScrollGrab] = FromRgb(0x3F86A8),
        };
        return WithDefaultGroups(p);
    }

    /// <summary>Frosted light grey with black text; the game highlights its lists in amber.</summary>
    private static Palette BuildClearWhite()
    {
        var accent = FromRgb(0x9C6A2F);
        var p = new Palette
        {
            [PaletteSlot.WindowBg] = FromRgb(0xD4D5DB, 0.93f),
            [PaletteSlot.SidebarBg] = FromRgb(0xC7C8CF, 0.94f),
            [PaletteSlot.HeaderBg] = FromRgb(0xE1E2E7, 0.95f),
            [PaletteSlot.InputBg] = FromRgb(0xF3F3F6),
            [PaletteSlot.PopupBg] = FromRgb(0xDADBE0, 0.98f),
            [PaletteSlot.Border] = FromRgb(0x8A8C92),
            [PaletteSlot.TitleBg] = FromRgb(0xC2C3CA),
            [PaletteSlot.TitleBgActive] = FromRgb(0xAEAFB7),
            [PaletteSlot.Text] = FromRgb(0x1A1A1A),
            [PaletteSlot.TextMuted] = FromRgb(0x5E5E5E),
            [PaletteSlot.Accent] = accent,
            [PaletteSlot.AccentText] = FromRgb(0xFFFFFF),
            [PaletteSlot.RowHover] = new Vector4(0f, 0f, 0f, 0.06f),
            [PaletteSlot.RowSelected] = WithAlpha(accent, 0.22f),
            [PaletteSlot.BubbleIn] = FromRgb(0xF7F7F9),
            [PaletteSlot.BubbleInText] = FromRgb(0x1A1A1A),
            [PaletteSlot.BubbleOut] = FromRgb(0x6E7079),
            [PaletteSlot.BubbleOutText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Badge] = FromRgb(0xD9483B),
            [PaletteSlot.BadgeText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Notice] = FromRgb(0x5E5E5E),
            [PaletteSlot.Error] = FromRgb(0xB3261E),
            [PaletteSlot.Button] = FromRgb(0xB9BAC1),
            [PaletteSlot.ButtonHover] = FromRgb(0xAAABB3),
            [PaletteSlot.ButtonActive] = FromRgb(0x9B9CA5),
            [PaletteSlot.ScrollGrab] = FromRgb(0x7E7D7E),
        };
        return WithDefaultGroups(p);
    }

    /// <summary>Frosted deep green with white text and leaf-green lines.</summary>
    private static Palette BuildClearGreen()
    {
        var accent = FromRgb(0x8CD65A);
        var p = new Palette
        {
            [PaletteSlot.WindowBg] = FromRgb(0x1D4217, 0.92f),
            [PaletteSlot.SidebarBg] = FromRgb(0x163512, 0.93f),
            [PaletteSlot.HeaderBg] = FromRgb(0x285A20, 0.95f),
            [PaletteSlot.InputBg] = FromRgb(0x0F260B),
            [PaletteSlot.PopupBg] = FromRgb(0x1D4217, 0.97f),
            [PaletteSlot.Border] = FromRgb(0x5AA631),
            [PaletteSlot.TitleBg] = FromRgb(0x163512),
            [PaletteSlot.TitleBgActive] = FromRgb(0x285A20),
            [PaletteSlot.Text] = FromRgb(0xF4FFF0),
            [PaletteSlot.TextMuted] = FromRgb(0xB5D1AA),
            [PaletteSlot.Accent] = accent,
            [PaletteSlot.AccentText] = FromRgb(0x10260A),
            [PaletteSlot.RowHover] = new Vector4(1f, 1f, 1f, 0.07f),
            [PaletteSlot.RowSelected] = WithAlpha(accent, 0.24f),
            [PaletteSlot.BubbleIn] = FromRgb(0x2C5A23),
            [PaletteSlot.BubbleInText] = FromRgb(0xF4FFF0),
            [PaletteSlot.BubbleOut] = FromRgb(0x4A8A2A),
            [PaletteSlot.BubbleOutText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Badge] = FromRgb(0xFF7A59),
            [PaletteSlot.BadgeText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Notice] = FromRgb(0xB5D1AA),
            [PaletteSlot.Error] = FromRgb(0xFF9080),
            [PaletteSlot.Button] = FromRgb(0x3A6524),
            [PaletteSlot.ButtonHover] = FromRgb(0x467A2C),
            [PaletteSlot.ButtonActive] = FromRgb(0x528F34),
            [PaletteSlot.ScrollGrab] = FromRgb(0x78A857),
        };
        return WithDefaultGroups(p);
    }

    /// <summary>Frosted slate grey with white text; the game highlights its lists in a warm tan.</summary>
    private static Palette BuildClearGrey()
    {
        var accent = FromRgb(0xC9A57E);
        var p = new Palette
        {
            [PaletteSlot.WindowBg] = FromRgb(0x293039, 0.92f),
            [PaletteSlot.SidebarBg] = FromRgb(0x20262E, 0.93f),
            [PaletteSlot.HeaderBg] = FromRgb(0x353D48, 0.95f),
            [PaletteSlot.InputBg] = FromRgb(0x181C22),
            [PaletteSlot.PopupBg] = FromRgb(0x293039, 0.97f),
            [PaletteSlot.Border] = FromRgb(0x8E9096),
            [PaletteSlot.TitleBg] = FromRgb(0x20262E),
            [PaletteSlot.TitleBgActive] = FromRgb(0x353D48),
            [PaletteSlot.Text] = FromRgb(0xF1F3F6),
            [PaletteSlot.TextMuted] = FromRgb(0xAEB4BC),
            [PaletteSlot.Accent] = accent,
            [PaletteSlot.AccentText] = FromRgb(0x241C14),
            [PaletteSlot.RowHover] = new Vector4(1f, 1f, 1f, 0.07f),
            [PaletteSlot.RowSelected] = WithAlpha(accent, 0.24f),
            [PaletteSlot.BubbleIn] = FromRgb(0x3A424D),
            [PaletteSlot.BubbleInText] = FromRgb(0xF1F3F6),
            [PaletteSlot.BubbleOut] = FromRgb(0x5B6573),
            [PaletteSlot.BubbleOutText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Badge] = FromRgb(0xE0533D),
            [PaletteSlot.BadgeText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Notice] = FromRgb(0xAEB4BC),
            [PaletteSlot.Error] = FromRgb(0xFF8A7A),
            [PaletteSlot.Button] = FromRgb(0x444C57),
            [PaletteSlot.ButtonHover] = FromRgb(0x525B67),
            [PaletteSlot.ButtonActive] = FromRgb(0x606A77),
            [PaletteSlot.ScrollGrab] = FromRgb(0x7D848D),
        };
        return WithDefaultGroups(p);
    }

    /// <summary>Frosted pink with plum text, the one other light theme besides Light and Clear White.</summary>
    private static Palette BuildClearPink()
    {
        var accent = FromRgb(0xB8458C);
        var p = new Palette
        {
            [PaletteSlot.WindowBg] = FromRgb(0xF7C6E8, 0.93f),
            [PaletteSlot.SidebarBg] = FromRgb(0xEFB3DC, 0.94f),
            [PaletteSlot.HeaderBg] = FromRgb(0xFAD6EF, 0.95f),
            [PaletteSlot.InputBg] = FromRgb(0xFFF0FA),
            [PaletteSlot.PopupBg] = FromRgb(0xF8CDEB, 0.98f),
            [PaletteSlot.Border] = FromRgb(0xCE5D9C),
            [PaletteSlot.TitleBg] = FromRgb(0xEFB3DC),
            [PaletteSlot.TitleBgActive] = FromRgb(0xE598CE),
            [PaletteSlot.Text] = FromRgb(0x4C284C),
            [PaletteSlot.TextMuted] = FromRgb(0x876187),
            [PaletteSlot.Accent] = accent,
            [PaletteSlot.AccentText] = FromRgb(0xFFFFFF),
            [PaletteSlot.RowHover] = new Vector4(0f, 0f, 0f, 0.06f),
            [PaletteSlot.RowSelected] = WithAlpha(accent, 0.22f),
            [PaletteSlot.BubbleIn] = FromRgb(0xFFF5FC),
            [PaletteSlot.BubbleInText] = FromRgb(0x4C284C),
            [PaletteSlot.BubbleOut] = accent,
            [PaletteSlot.BubbleOutText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Badge] = FromRgb(0xC7303F),
            [PaletteSlot.BadgeText] = FromRgb(0xFFFFFF),
            [PaletteSlot.Notice] = FromRgb(0x876187),
            [PaletteSlot.Error] = FromRgb(0xB3261E),
            [PaletteSlot.Button] = FromRgb(0xEBA8D7),
            [PaletteSlot.ButtonHover] = FromRgb(0xE395CD),
            [PaletteSlot.ButtonActive] = FromRgb(0xDA82C2),
            [PaletteSlot.ScrollGrab] = FromRgb(0xB0578F),
        };
        return WithDefaultGroups(p);
    }

    private static Palette WithDefaultGroups(Palette palette)
    {
        palette[PaletteSlot.Tell] = DefaultTell;
        palette[PaletteSlot.Linkshell] = DefaultLinkshell;
        palette[PaletteSlot.CrossWorld] = DefaultCrossWorld;
        palette[PaletteSlot.FreeCompany] = DefaultFreeCompany;
        return palette;
    }
}
