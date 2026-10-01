using System.Numerics;
using Parley.Core;
using Parley.Core.Settings;
using Parley.Core.Theme;
using Xunit;

namespace Parley.Tests;

public class ColourMathTests
{
    private static void AssertClose(Vector4 expected, Vector4 actual, float tolerance = 0.003f)
    {
        Assert.True(Vector4.Distance(expected, actual) <= tolerance, $"Expected {expected}, got {actual}");
    }

    [Fact]
    public void Abgr_is_red_in_the_low_byte_and_round_trips()
    {
        var colour = ColourMath.FromAbgr(0x80332211);

        AssertClose(new Vector4(0x11 / 255f, 0x22 / 255f, 0x33 / 255f, 0x80 / 255f), colour);
        Assert.Equal(0x80332211u, ColourMath.ToAbgr(colour));
    }

    [Fact]
    public void Rgb_is_red_in_the_high_byte()
    {
        AssertClose(new Vector4(1f, 0xB8 / 255f, 0xDE / 255f, 1f), ColourMath.FromRgb(0xFFB8DE));
    }

    [Theory]
    [InlineData("#FFB8DE", 1f, 1f)]
    [InlineData("ffb8de", 1f, 1f)]
    [InlineData("#FFB8DE80", 1f, 0x80 / 255f)]
    [InlineData("  #ffb8de80  ", 1f, 0x80 / 255f)]
    public void Hex_is_read_with_or_without_alpha_and_hash(string text, float red, float alpha)
    {
        Assert.True(ColourMath.TryParseHex(text, out var colour));
        Assert.Equal(red, colour.X, 3);
        Assert.Equal(alpha, colour.W, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#123456789")]
    public void Malformed_hex_is_rejected(string? text)
    {
        Assert.False(ColourMath.TryParseHex(text, out _));
    }

    [Fact]
    public void Hex_round_trips()
    {
        var hex = ColourMath.ToHex(ColourMath.FromRgb(0x2A4B6C, 0.5f));

        Assert.Equal("#2A4B6C80", hex);
        Assert.True(ColourMath.TryParseHex(hex, out var parsed));
        Assert.Equal(hex, ColourMath.ToHex(parsed));
    }

    [Fact]
    public void Readable_text_is_light_on_dark_and_dark_on_light()
    {
        Assert.True(ColourMath.Luminance(ColourMath.ReadableOn(ColourMath.FromRgb(0x202020))) > 0.9f);
        Assert.True(ColourMath.Luminance(ColourMath.ReadableOn(ColourMath.FromRgb(0xF0F0F0))) < 0.2f);
    }

    [Fact]
    public void Shift_moves_away_from_the_colours_own_end_of_the_scale()
    {
        var dark = ColourMath.FromRgb(0x202020);
        var light = ColourMath.FromRgb(0xE0E0E0);

        Assert.True(ColourMath.Luminance(ColourMath.Shift(dark, 0.2f)) > ColourMath.Luminance(dark));
        Assert.True(ColourMath.Luminance(ColourMath.Shift(light, 0.2f)) < ColourMath.Luminance(light));
        Assert.Equal(0.5f, ColourMath.Shift(ColourMath.FromRgb(0x202020, 0.5f), 0.2f).W, 3);
    }

    [Fact]
    public void An_accent_that_would_vanish_into_the_background_is_pulled_toward_the_text()
    {
        var green = GameThemes.DefaultLinkshell;
        var lightBackground = ColourMath.FromRgb(0xECE7DB);
        var darkBackground = ColourMath.FromRgb(0x2A2A2A);
        var darkText = ColourMath.FromRgb(0x2F2A24);

        var onLight = ColourMath.LegibleOn(green, lightBackground, darkText);
        var onDark = ColourMath.LegibleOn(green, darkBackground, ColourMath.FromRgb(0xEEEEEE));

        Assert.True(ColourMath.Luminance(onLight) < ColourMath.Luminance(green));
        Assert.Equal(green, onDark);
    }

    [Fact]
    public void A_name_always_gets_the_same_colour_whatever_its_case()
    {
        var first = ColourMath.ForName("Alice Smith", onDarkBackground: true);

        Assert.Equal(first, ColourMath.ForName("alice smith", onDarkBackground: true));
        Assert.NotEqual(first, ColourMath.ForName("Bob Jones", onDarkBackground: true));
        // Bright on dark themes, deep on light ones.
        Assert.True(ColourMath.Luminance(first) > ColourMath.Luminance(ColourMath.ForName("Alice Smith", onDarkBackground: false)));
    }

    [Theory]
    [InlineData(0f, 1f, 0f, 0f)]
    [InlineData(1f / 3f, 0f, 1f, 0f)]
    [InlineData(2f / 3f, 0f, 0f, 1f)]
    [InlineData(1f, 1f, 0f, 0f)]
    public void Hsv_primaries(float hue, float red, float green, float blue)
    {
        AssertClose(new Vector4(red, green, blue, 1f), ColourMath.FromHsv(hue, 1f, 1f), 0.01f);
    }
}

public class PaletteTests
{
    [Fact]
    public void A_palette_round_trips_through_its_saved_form()
    {
        var original = GameThemes.For(GameThemes.ClassicFf);

        var restored = new Palette();
        restored.ApplyHex(original.ToHex());

        foreach (var slot in Palette.Slots)
            Assert.Equal(ColourMath.ToHex(original[slot]), ColourMath.ToHex(restored[slot]));
    }

    [Fact]
    public void A_saved_theme_that_lacks_a_slot_keeps_what_was_there()
    {
        var palette = GameThemes.For(GameThemes.Dark);
        var before = palette.Clone();

        palette.ApplyHex(new Dictionary<string, string>
        {
            ["Accent"] = "#112233FF",
            ["Text"] = "not a colour",
            ["NoSuchSlot"] = "#FFFFFFFF",
        });

        Assert.Equal("#112233FF", ColourMath.ToHex(palette.Accent));
        Assert.Equal(before.Text, palette.Text);
        Assert.Equal(before.WindowBg, palette.WindowBg);
    }

    [Fact]
    public void Cloning_does_not_share_storage()
    {
        var palette = GameThemes.For(GameThemes.Dark);
        var copy = palette.Clone();

        copy[PaletteSlot.Accent] = new Vector4(0, 0, 0, 1);

        Assert.NotEqual(copy.Accent, palette.Accent);
    }

    [Fact]
    public void Every_slot_has_a_label_for_the_editor()
    {
        var labels = Palette.Slots.Select(Palette.Label).ToArray();

        Assert.All(labels, label => Assert.False(string.IsNullOrWhiteSpace(label)));
        // Two slots with one label could not be told apart in the editor.
        Assert.Equal(labels.Length, labels.Distinct().Count());
        // A slot added without a label falls through to its enum name. For a
        // name like "WindowBg" that is not something to show a person.
        foreach (var slot in Palette.Slots)
        {
            var name = slot.ToString();
            if (name.Skip(1).Any(char.IsUpper)) Assert.NotEqual(name, Palette.Label(slot));
        }
    }

    [Fact]
    public void Group_colours_map_to_their_slots()
    {
        var palette = GameThemes.For(GameThemes.Dark);

        Assert.Equal(palette[PaletteSlot.Tell], palette.Group(ChannelGroup.Tell));
        Assert.Equal(palette[PaletteSlot.Linkshell], palette.Group(ChannelGroup.Linkshell));
        Assert.Equal(palette[PaletteSlot.CrossWorld], palette.Group(ChannelGroup.CrossWorld));
        Assert.Equal(PaletteSlot.CrossWorld, Palette.GroupSlot(ChannelGroup.CrossWorld));
    }
}

public class GameThemeTests
{
    public static TheoryData<int> Themes =>
    [
        GameThemes.Dark, GameThemes.Light, GameThemes.ClassicFf, GameThemes.ClearBlue,
        GameThemes.ClearWhite, GameThemes.ClearGreen, GameThemes.ClearGrey, GameThemes.ClearPink,
    ];

    private static float Contrast(Vector4 a, Vector4 b) => MathF.Abs(ColourMath.Luminance(a) - ColourMath.Luminance(b));

    [Theory]
    [MemberData(nameof(Themes))]
    public void Every_slot_is_filled_in(int theme)
    {
        var palette = GameThemes.For(theme);

        foreach (var slot in Palette.Slots)
            Assert.True(palette[slot].W > 0f, $"{GameThemes.Name(theme)} leaves {slot} transparent");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_can_be_read_against_what_it_sits_on(int theme)
    {
        var p = GameThemes.For(theme);

        Assert.True(Contrast(p.Text, p.WindowBg) > 0.45f);
        Assert.True(Contrast(p.Text, p.SidebarBg) > 0.45f);
        Assert.True(Contrast(p.Text, p.InputBg) > 0.45f);
        Assert.True(Contrast(p.TextMuted, p.WindowBg) > 0.2f);
        Assert.True(Contrast(p.BubbleInText, p.BubbleIn) > 0.4f);
        Assert.True(Contrast(p.BubbleOutText, p.BubbleOut) > 0.35f);
        Assert.True(Contrast(p.BadgeText, p.Badge) > 0.35f);
        Assert.True(Contrast(p.AccentText, p.Accent) > 0.35f);
    }

    [Fact]
    public void The_themes_with_dark_text_are_the_light_ones()
    {
        // As the game has them: Light, Clear White and Clear Pink put dark
        // text on a pale window; the other five are dark.
        Assert.True(GameThemes.For(GameThemes.Dark).IsDark);
        Assert.False(GameThemes.For(GameThemes.Light).IsDark);
        Assert.True(GameThemes.For(GameThemes.ClassicFf).IsDark);
        Assert.True(GameThemes.For(GameThemes.ClearBlue).IsDark);
        Assert.False(GameThemes.For(GameThemes.ClearWhite).IsDark);
        Assert.True(GameThemes.For(GameThemes.ClearGreen).IsDark);
        Assert.True(GameThemes.For(GameThemes.ClearGrey).IsDark);
        Assert.False(GameThemes.For(GameThemes.ClearPink).IsDark);
    }

    [Fact]
    public void Every_theme_the_game_offers_has_its_own_name_and_look()
    {
        var names = new HashSet<string>();
        var windows = new HashSet<Vector4>();
        for (var theme = 0; theme < GameThemes.Count; theme++)
        {
            Assert.True(names.Add(GameThemes.Name(theme)), $"theme {theme} shares a name");
            Assert.True(windows.Add(GameThemes.For(theme).WindowBg), $"{GameThemes.Name(theme)} looks like another theme");
        }
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(5, 5)]
    [InlineData(7, 7)]
    [InlineData(8, -1)]
    [InlineData(-4, -1)]
    public void A_theme_picked_for_Parley_alone_is_kept_only_if_the_game_has_it(int picked, int kept)
    {
        var configuration = new Configuration { GameThemeOverride = picked };
        configuration.Clamp();
        Assert.Equal(kept, configuration.GameThemeOverride);
    }

    [Fact]
    public void An_unknown_theme_number_falls_back_to_dark()
    {
        Assert.Equal(GameThemes.For(GameThemes.Dark).WindowBg, GameThemes.For(99).WindowBg);
        Assert.Equal("Dark", GameThemes.Name(99));
    }
}

public class UmbraThemeMapperTests
{
    // A few of Umbra's own defaults, as it registers them (0xAABBGGRR).
    private static readonly Dictionary<string, uint> UmbraDefaults = new()
    {
        ["Window.Background"] = 0xFF212021,
        ["Window.BackgroundLight"] = 0xFF292829,
        ["Window.Border"] = 0xFF484848,
        ["Window.TitlebarBackground"] = 0xFF101010,
        ["Window.TitlebarGradient1"] = 0xFF2F2E2F,
        ["Window.ScrollbarThumb"] = 0xFF484848,
        ["Window.Text"] = 0xFFD0D0D0,
        ["Window.TextMuted"] = 0xB0C0C0C0,
        ["Window.AccentColor"] = 0xFF4C8EB9,
        ["Input.Background"] = 0xFF151515,
        ["Widget.Background"] = 0xFF101010,
        ["Widget.BackgroundHover"] = 0xFF2F2F2F,
        ["Widget.PopupBackground"] = 0xFF101010,
    };

    [Fact]
    public void Umbra_colours_land_in_the_matching_slots()
    {
        var palette = UmbraThemeMapper.Map(UmbraDefaults);

        Assert.Equal(0xFF212021u, ColourMath.ToAbgr(palette.WindowBg));
        Assert.Equal(0xFF292829u, ColourMath.ToAbgr(palette.HeaderBg));
        Assert.Equal(0xFF484848u, ColourMath.ToAbgr(palette.Border));
        Assert.Equal(0xFFD0D0D0u, ColourMath.ToAbgr(palette.Text));
        Assert.Equal(0xB0C0C0C0u, ColourMath.ToAbgr(palette.TextMuted));
        Assert.Equal(0xFF4C8EB9u, ColourMath.ToAbgr(palette.Accent));
        Assert.Equal(0xFF151515u, ColourMath.ToAbgr(palette.InputBg));
        Assert.Equal(0xFF101010u, ColourMath.ToAbgr(palette.TitleBg));
    }

    [Fact]
    public void Derived_colours_stay_readable()
    {
        var p = UmbraThemeMapper.Map(UmbraDefaults);

        Assert.True(MathF.Abs(ColourMath.Luminance(p.BubbleOutText) - ColourMath.Luminance(p.BubbleOut)) > 0.35f);
        Assert.True(MathF.Abs(ColourMath.Luminance(p.BubbleInText) - ColourMath.Luminance(p.BubbleIn)) > 0.4f);
        Assert.True(MathF.Abs(ColourMath.Luminance(p.BadgeText) - ColourMath.Luminance(p.Badge)) > 0.35f);
        Assert.Equal(1f, p.BubbleIn.W);
    }

    [Fact]
    public void A_light_umbra_profile_gets_dark_text_on_its_accents()
    {
        var light = new Dictionary<string, uint>(UmbraDefaults)
        {
            ["Window.Background"] = 0xFFF0F0F0,
            ["Window.BackgroundLight"] = 0xFFFFFFFF,
            ["Window.Text"] = 0xFF202020,
            ["Window.AccentColor"] = 0xFFE0D8A0,
        };

        var palette = UmbraThemeMapper.Map(light);

        Assert.False(palette.IsDark);
        Assert.True(ColourMath.Luminance(palette.AccentText) < 0.2f);
    }

    [Fact]
    public void A_profile_missing_names_borrows_from_the_dark_theme_instead_of_failing()
    {
        var palette = UmbraThemeMapper.Map(new Dictionary<string, uint>());
        var dark = GameThemes.For(GameThemes.Dark);

        Assert.Equal(dark.WindowBg, palette.WindowBg);
        Assert.Equal(dark.Text, palette.Text);
        foreach (var slot in Palette.Slots) Assert.True(palette[slot].W > 0f);
    }
}
