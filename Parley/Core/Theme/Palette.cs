using System.Numerics;

namespace Parley.Core.Theme;

/// <summary>Every colour the chat window draws with. Order is display order in the custom theme editor.</summary>
public enum PaletteSlot
{
    WindowBg,
    SidebarBg,
    HeaderBg,
    InputBg,
    PopupBg,
    Border,
    TitleBg,
    TitleBgActive,
    Text,
    TextMuted,
    Accent,
    AccentText,
    RowHover,
    RowSelected,
    BubbleIn,
    BubbleInText,
    BubbleOut,
    BubbleOutText,
    Badge,
    BadgeText,
    Notice,
    Error,
    Button,
    ButtonHover,
    ButtonActive,
    ScrollGrab,
    Tell,
    Linkshell,
    CrossWorld,
    FreeCompany,
}

public sealed class Palette
{
    public static readonly PaletteSlot[] Slots = Enum.GetValues<PaletteSlot>();

    private readonly Vector4[] colours = new Vector4[Slots.Length];

    public Vector4 this[PaletteSlot slot]
    {
        get => colours[(int)slot];
        set => colours[(int)slot] = value;
    }

    public Vector4 WindowBg => this[PaletteSlot.WindowBg];
    public Vector4 SidebarBg => this[PaletteSlot.SidebarBg];
    public Vector4 HeaderBg => this[PaletteSlot.HeaderBg];
    public Vector4 InputBg => this[PaletteSlot.InputBg];
    public Vector4 PopupBg => this[PaletteSlot.PopupBg];
    public Vector4 Border => this[PaletteSlot.Border];
    public Vector4 TitleBg => this[PaletteSlot.TitleBg];
    public Vector4 TitleBgActive => this[PaletteSlot.TitleBgActive];
    public Vector4 Text => this[PaletteSlot.Text];
    public Vector4 TextMuted => this[PaletteSlot.TextMuted];
    public Vector4 Accent => this[PaletteSlot.Accent];
    public Vector4 AccentText => this[PaletteSlot.AccentText];
    public Vector4 RowHover => this[PaletteSlot.RowHover];
    public Vector4 RowSelected => this[PaletteSlot.RowSelected];
    public Vector4 BubbleIn => this[PaletteSlot.BubbleIn];
    public Vector4 BubbleInText => this[PaletteSlot.BubbleInText];
    public Vector4 BubbleOut => this[PaletteSlot.BubbleOut];
    public Vector4 BubbleOutText => this[PaletteSlot.BubbleOutText];
    public Vector4 Badge => this[PaletteSlot.Badge];
    public Vector4 BadgeText => this[PaletteSlot.BadgeText];
    public Vector4 Notice => this[PaletteSlot.Notice];
    public Vector4 Error => this[PaletteSlot.Error];
    public Vector4 Button => this[PaletteSlot.Button];
    public Vector4 ButtonHover => this[PaletteSlot.ButtonHover];
    public Vector4 ButtonActive => this[PaletteSlot.ButtonActive];
    public Vector4 ScrollGrab => this[PaletteSlot.ScrollGrab];

    public Vector4 Group(ChannelGroup group) => this[GroupSlot(group)];

    public static PaletteSlot GroupSlot(ChannelGroup group) => group switch
    {
        ChannelGroup.Tell => PaletteSlot.Tell,
        ChannelGroup.Linkshell => PaletteSlot.Linkshell,
        ChannelGroup.FreeCompany => PaletteSlot.FreeCompany,
        _ => PaletteSlot.CrossWorld,
    };

    /// <summary>Whether this is a light-on-dark theme. Decides things like which way hover states shift.</summary>
    public bool IsDark => ColourMath.IsDark(WindowBg);

    public Palette Clone()
    {
        var copy = new Palette();
        Array.Copy(colours, copy.colours, colours.Length);
        return copy;
    }

    public void CopyFrom(Palette other) => Array.Copy(other.colours, colours, colours.Length);

    /// <summary>The palette as "#RRGGBBAA" strings keyed by slot name, which is how a custom theme is saved.</summary>
    public Dictionary<string, string> ToHex()
    {
        var map = new Dictionary<string, string>(Slots.Length);
        foreach (var slot in Slots) map[slot.ToString()] = ColourMath.ToHex(this[slot]);
        return map;
    }

    /// <summary>
    /// Overlays saved colours on this palette. Slots the file does not mention
    /// keep what they had, so a theme saved before a slot existed still loads.
    /// </summary>
    public void ApplyHex(IReadOnlyDictionary<string, string> map)
    {
        foreach (var slot in Slots)
        {
            if (map.TryGetValue(slot.ToString(), out var hex) && ColourMath.TryParseHex(hex, out var colour))
                this[slot] = colour;
        }
    }

    public static string Label(PaletteSlot slot) => slot switch
    {
        PaletteSlot.WindowBg => "Window background",
        PaletteSlot.SidebarBg => "Sidebar background",
        PaletteSlot.HeaderBg => "Header background",
        PaletteSlot.InputBg => "Input background",
        PaletteSlot.PopupBg => "Menu background",
        PaletteSlot.Border => "Borders and dividers",
        PaletteSlot.TitleBg => "Title bar",
        PaletteSlot.TitleBgActive => "Title bar, focused",
        PaletteSlot.Text => "Text",
        PaletteSlot.TextMuted => "Secondary text",
        PaletteSlot.Accent => "Accent",
        PaletteSlot.AccentText => "Text on accent",
        PaletteSlot.RowHover => "Row under the cursor",
        PaletteSlot.RowSelected => "Selected row",
        PaletteSlot.BubbleIn => "Their messages",
        PaletteSlot.BubbleInText => "Their message text",
        PaletteSlot.BubbleOut => "Your messages",
        PaletteSlot.BubbleOutText => "Your message text",
        PaletteSlot.Badge => "Unread badge",
        PaletteSlot.BadgeText => "Unread badge text",
        PaletteSlot.Notice => "Notices",
        PaletteSlot.Error => "Errors",
        PaletteSlot.Button => "Buttons",
        PaletteSlot.ButtonHover => "Button under the cursor",
        PaletteSlot.ButtonActive => "Button pressed",
        PaletteSlot.ScrollGrab => "Scrollbar",
        PaletteSlot.Tell => "Tells",
        PaletteSlot.Linkshell => "Linkshells",
        PaletteSlot.CrossWorld => "Cross-world linkshells",
        PaletteSlot.FreeCompany => "Free Company",
        _ => slot.ToString(),
    };
}
