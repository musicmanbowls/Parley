using System.Numerics;
using Dalamud.Bindings.ImGui;
using Parley.Core;
using Parley.Core.Settings;
using Parley.Core.Theme;
using Parley.Game;
using static Parley.Core.Theme.ColourMath;

namespace Parley.Ui;

/// <summary>
/// Decides what colours the chat window draws with this frame, from one of
/// four sources: Dalamud's current style, the game's UI theme, Umbra's colour
/// profile, or the user's own.
///
/// In Dalamud mode nothing is pushed on to ImGui at all; the palette is read
/// back out of the live style so the parts Parley draws by hand (bubbles,
/// badges, the sidebar) match a window that is otherwise stock.
/// </summary>
internal sealed class ThemeManager
{
    private static readonly Vector4 BadgeRed = FromRgb(0xDC4A3D);
    private static readonly Vector4 ErrorRed = FromRgb(0xFF7A6B);

    private readonly Configuration config;
    private readonly GameColours gameColours;

    /// <summary>What <see cref="Palette"/> points at. Rewritten every frame, so nothing else may hold on to it.</summary>
    private readonly Palette resolved = new();
    private readonly Palette live = new();
    private Palette? gamePalette;
    private int gamePaletteTheme = -1;
    private Palette? umbraPalette;
    private Palette? customPalette;
    private int colourPushes;
    private int stylePushes;

    public ThemeManager(Configuration config, GameColours gameColours)
    {
        this.config = config;
        this.gameColours = gameColours;
        resolved.CopyFrom(GameThemes.For(GameThemes.Dark));
    }

    public Palette Palette => resolved;

    /// <summary>False in Dalamud mode, and in Umbra mode until Umbra has supplied its colours.</summary>
    public bool Themed { get; private set; }

    /// <summary>Whatever the Umbra companion last sent as a token, or 0 if it has sent nothing.</summary>
    public long UmbraToken { get; private set; }

    public bool UmbraAvailable => umbraPalette != null;

    /// <summary>A copy of the palette built from Umbra's colours, to start a custom theme from. Null if none was sent.</summary>
    public Palette? CloneUmbra() => umbraPalette?.Clone();

    public int GameTheme => gameColours.Theme;

    /// <summary>The game theme the window looks like: the one picked for Parley in its settings, or else the game's own.</summary>
    public int EffectiveGameTheme => GameThemes.IsKnown(config.GameThemeOverride) ? config.GameThemeOverride : gameColours.Theme;

    /// <summary>
    /// Which column of the game's UI colour table to read for text on this
    /// background: the game theme's own while the window looks like that theme
    /// and the background is as dark or light as its windows; otherwise the
    /// dark or light theme's, whichever suits the background.
    /// </summary>
    public int UiColourTheme(Vector4 background)
    {
        var dark = IsDark(background);
        if (config.Theme == ThemeMode.Game)
        {
            var game = EffectiveGameTheme;
            var lightTheme = game is GameThemes.Light or GameThemes.ClearWhite or GameThemes.ClearPink;
            if (dark != lightTheme) return game;
        }
        return dark ? GameThemes.Dark : GameThemes.Light;
    }

    public void SetUmbra(IReadOnlyDictionary<string, uint> colours, long token)
    {
        umbraPalette = UmbraThemeMapper.Map(colours);
        UmbraToken = token;
    }

    /// <summary>Call after editing <see cref="Configuration.CustomColours"/>.</summary>
    public void InvalidateCustom() => customPalette = null;

    /// <summary>Works out this frame's palette. Call once, before anything is drawn.</summary>
    public void Resolve()
    {
        Palette source;
        switch (config.Theme)
        {
            case ThemeMode.Game:
                var theme = EffectiveGameTheme;
                if (gamePalette == null || gamePaletteTheme != theme)
                {
                    gamePalette = GameThemes.For(theme);
                    gamePaletteTheme = theme;
                }
                source = gamePalette;
                Themed = true;
                break;

            case ThemeMode.Umbra when umbraPalette != null:
                source = umbraPalette;
                Themed = true;
                break;

            case ThemeMode.Custom:
                source = customPalette ??= BuildCustom();
                Themed = true;
                break;

            default:
                ReadImGuiStyle(live);
                source = live;
                Themed = false;
                break;
        }

        resolved.CopyFrom(source);

        if (config.UseGameChatColours)
        {
            foreach (var group in ChannelGroups.All)
                resolved[Palette.GroupSlot(group)] = gameColours.Group(group);
        }
    }

    /// <summary>The colour for one conversation: its own chat colour if the game has one, else its group's.</summary>
    public Vector4 ColourFor(Conversation conversation)
    {
        if (config.UseGameChatColours && !conversation.IsTell && conversation.Slot > 0)
            return gameColours.Slot(conversation.Group, conversation.Slot);
        return resolved.Group(conversation.Group);
    }

    /// <summary>Pushes the palette on to ImGui for the window about to be drawn. No-op in Dalamud mode.</summary>
    public void Push()
    {
        if (!Themed) return;

        var p = resolved;
        var rounding = config.CornerRounding * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;

        Colour(ImGuiCol.WindowBg, p.WindowBg);
        Colour(ImGuiCol.ChildBg, Vector4.Zero);
        Colour(ImGuiCol.PopupBg, p.PopupBg);
        Colour(ImGuiCol.Border, p.Border);
        Colour(ImGuiCol.Separator, p.Border);
        Colour(ImGuiCol.Text, p.Text);
        Colour(ImGuiCol.TextDisabled, p.TextMuted);
        Colour(ImGuiCol.TextSelectedBg, WithAlpha(p.Accent, 0.35f));
        Colour(ImGuiCol.FrameBg, p.InputBg);
        Colour(ImGuiCol.FrameBgHovered, Shift(p.InputBg, 0.08f));
        Colour(ImGuiCol.FrameBgActive, Shift(p.InputBg, 0.12f));
        Colour(ImGuiCol.TitleBg, p.TitleBg);
        Colour(ImGuiCol.TitleBgActive, p.TitleBgActive);
        Colour(ImGuiCol.TitleBgCollapsed, WithAlpha(p.TitleBg, 0.8f));
        Colour(ImGuiCol.ScrollbarBg, Vector4.Zero);
        Colour(ImGuiCol.ScrollbarGrab, p.ScrollGrab);
        Colour(ImGuiCol.ScrollbarGrabHovered, Shift(p.ScrollGrab, 0.12f));
        Colour(ImGuiCol.ScrollbarGrabActive, Shift(p.ScrollGrab, 0.22f));
        Colour(ImGuiCol.Button, p.Button);
        Colour(ImGuiCol.ButtonHovered, p.ButtonHover);
        Colour(ImGuiCol.ButtonActive, p.ButtonActive);
        Colour(ImGuiCol.Header, p.RowSelected);
        Colour(ImGuiCol.HeaderHovered, p.RowHover);
        Colour(ImGuiCol.HeaderActive, p.RowSelected);
        Colour(ImGuiCol.CheckMark, p.Accent);
        Colour(ImGuiCol.SliderGrab, p.Accent);
        Colour(ImGuiCol.SliderGrabActive, Shift(p.Accent, 0.15f));
        Colour(ImGuiCol.ResizeGrip, WithAlpha(p.Accent, 0.20f));
        Colour(ImGuiCol.ResizeGripHovered, WithAlpha(p.Accent, 0.55f));
        Colour(ImGuiCol.ResizeGripActive, WithAlpha(p.Accent, 0.85f));
        Colour(ImGuiCol.NavHighlight, p.Accent);

        Style(ImGuiStyleVar.WindowRounding, rounding);
        Style(ImGuiStyleVar.ChildRounding, rounding);
        Style(ImGuiStyleVar.PopupRounding, rounding);
        Style(ImGuiStyleVar.FrameRounding, MathF.Min(rounding, 6f * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale));
        Style(ImGuiStyleVar.ScrollbarRounding, rounding);
        Style(ImGuiStyleVar.WindowBorderSize, 1f);
    }

    public void Pop()
    {
        if (colourPushes > 0) ImGui.PopStyleColor(colourPushes);
        if (stylePushes > 0) ImGui.PopStyleVar(stylePushes);
        colourPushes = 0;
        stylePushes = 0;
    }

    private void Colour(ImGuiCol slot, Vector4 colour)
    {
        ImGui.PushStyleColor(slot, colour);
        colourPushes++;
    }

    private void Style(ImGuiStyleVar slot, float value)
    {
        ImGui.PushStyleVar(slot, value);
        stylePushes++;
    }

    private Palette BuildCustom()
    {
        var palette = GameThemes.For(GameThemes.Dark);
        palette.ApplyHex(config.CustomColours);
        return palette;
    }

    /// <summary>Derives a palette from whatever style ImGui currently has, Dalamud's own or a user's.</summary>
    private static void ReadImGuiStyle(Palette p)
    {
        var colours = ImGui.GetStyle().Colors;

        var window = At(colours, ImGuiCol.WindowBg);
        var solid = window with { W = 1f };
        var text = At(colours, ImGuiCol.Text);
        var accent = PickAccent(colours);
        var hasAccent = accent.HasValue;

        p[PaletteSlot.WindowBg] = window;
        p[PaletteSlot.SidebarBg] = Tint(window, new Vector4(0f, 0f, 0f, 1f), IsDark(window) ? 0.22f : 0.06f);
        p[PaletteSlot.HeaderBg] = Shift(window, 0.06f);
        p[PaletteSlot.InputBg] = At(colours, ImGuiCol.FrameBg);
        p[PaletteSlot.PopupBg] = At(colours, ImGuiCol.PopupBg);
        p[PaletteSlot.Border] = At(colours, ImGuiCol.Border);
        p[PaletteSlot.TitleBg] = At(colours, ImGuiCol.TitleBg);
        p[PaletteSlot.TitleBgActive] = At(colours, ImGuiCol.TitleBgActive);
        p[PaletteSlot.Text] = text;
        p[PaletteSlot.TextMuted] = At(colours, ImGuiCol.TextDisabled);
        p[PaletteSlot.Accent] = accent ?? text;
        p[PaletteSlot.AccentText] = ReadableOn(accent ?? text);
        var hover = At(colours, ImGuiCol.HeaderHovered);
        p[PaletteSlot.RowHover] = WithAlpha(hover, MathF.Min(hover.W, 0.45f));
        p[PaletteSlot.RowSelected] = At(colours, ImGuiCol.Header);
        p[PaletteSlot.BubbleIn] = Shift(solid, 0.10f);
        p[PaletteSlot.BubbleInText] = text;

        // A style whose highlights are all greys gets a second, lighter grey
        // for outgoing bubbles rather than an invented hue.
        var bubbleOut = hasAccent ? Mix(accent!.Value with { W = 1f }, solid, 0.45f) : Shift(solid, 0.24f);
        p[PaletteSlot.BubbleOut] = bubbleOut;
        p[PaletteSlot.BubbleOutText] = ReadableOn(bubbleOut);
        p[PaletteSlot.Badge] = BadgeRed;
        p[PaletteSlot.BadgeText] = new Vector4(1f, 1f, 1f, 1f);
        p[PaletteSlot.Notice] = At(colours, ImGuiCol.TextDisabled);
        p[PaletteSlot.Error] = IsDark(window) ? ErrorRed : FromRgb(0xC0392B);
        p[PaletteSlot.Button] = At(colours, ImGuiCol.Button);
        p[PaletteSlot.ButtonHover] = At(colours, ImGuiCol.ButtonHovered);
        p[PaletteSlot.ButtonActive] = At(colours, ImGuiCol.ButtonActive);
        p[PaletteSlot.ScrollGrab] = At(colours, ImGuiCol.ScrollbarGrab);
        p[PaletteSlot.Tell] = GameThemes.DefaultTell;
        p[PaletteSlot.Linkshell] = GameThemes.DefaultLinkshell;
        p[PaletteSlot.CrossWorld] = GameThemes.DefaultCrossWorld;
        p[PaletteSlot.FreeCompany] = GameThemes.DefaultFreeCompany;
    }

    private static Vector4 At(Span<Vector4> colours, ImGuiCol slot) => colours[(int)slot];

    /// <summary>The most saturated of the style's highlight colours, or null if they are all near enough grey.</summary>
    private static Vector4? PickAccent(Span<Vector4> colours)
    {
        ReadOnlySpan<ImGuiCol> candidates =
        [
            ImGuiCol.CheckMark, ImGuiCol.SliderGrabActive, ImGuiCol.ButtonActive, ImGuiCol.HeaderActive, ImGuiCol.TabActive,
        ];

        Vector4? best = null;
        var bestSaturation = 0.25f;
        foreach (var candidate in candidates)
        {
            var colour = colours[(int)candidate];
            var max = MathF.Max(colour.X, MathF.Max(colour.Y, colour.Z));
            var min = MathF.Min(colour.X, MathF.Min(colour.Y, colour.Z));
            if (max <= 0.2f || colour.W < 0.5f) continue;

            var saturation = (max - min) / max;
            if (saturation <= bestSaturation) continue;
            bestSaturation = saturation;
            best = colour;
        }
        return best;
    }
}
