using System.Globalization;
using System.Numerics;

namespace Parley.Core.Theme;

/// <summary>Colour arithmetic on RGBA vectors, each channel 0-1.</summary>
public static class ColourMath
{
    /// <summary>From 0xAABBGGRR, the packing both ImGui and Umbra use.</summary>
    public static Vector4 FromAbgr(uint abgr) => new(
        (abgr & 0xFF) / 255f,
        ((abgr >> 8) & 0xFF) / 255f,
        ((abgr >> 16) & 0xFF) / 255f,
        ((abgr >> 24) & 0xFF) / 255f);

    public static uint ToAbgr(Vector4 colour) =>
        ToByte(colour.X) | (ToByte(colour.Y) << 8) | (ToByte(colour.Z) << 16) | (ToByte(colour.W) << 24);

    /// <summary>From 0xRRGGBB, the way colours are written in a stylesheet and the way the game stores chat colours.</summary>
    public static Vector4 FromRgb(uint rgb, float alpha = 1f) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        alpha);

    public static string ToHex(Vector4 colour) =>
        $"#{ToByte(colour.X):X2}{ToByte(colour.Y):X2}{ToByte(colour.Z):X2}{ToByte(colour.W):X2}";

    /// <summary>Accepts "#RRGGBB" and "#RRGGBBAA", with or without the hash.</summary>
    public static bool TryParseHex(string? text, out Vector4 colour)
    {
        colour = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var hex = text.AsSpan().Trim().TrimStart('#');
        if (hex.Length is not (6 or 8)) return false;
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;

        colour = hex.Length == 6
            ? FromRgb(value)
            : new Vector4(((value >> 24) & 0xFF) / 255f, ((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f);
        return true;
    }

    public static Vector4 WithAlpha(Vector4 colour, float alpha) => colour with { W = alpha };

    /// <summary>Linear blend of all four channels; 0 gives <paramref name="a"/>, 1 gives <paramref name="b"/>.</summary>
    public static Vector4 Mix(Vector4 a, Vector4 b, float amount) => Vector4.Lerp(a, b, Math.Clamp(amount, 0f, 1f));

    /// <summary>Blends the colour channels toward another colour and keeps this one's alpha.</summary>
    public static Vector4 Tint(Vector4 colour, Vector4 toward, float amount) =>
        Mix(colour, toward, amount) with { W = colour.W };

    /// <summary>Perceived brightness, 0 black to 1 white.</summary>
    public static float Luminance(Vector4 colour) => (0.2126f * colour.X) + (0.7152f * colour.Y) + (0.0722f * colour.Z);

    public static bool IsDark(Vector4 colour) => Luminance(colour) < 0.5f;

    /// <summary>Near-white or near-black text, whichever reads on the background.</summary>
    public static Vector4 ReadableOn(Vector4 background) =>
        IsDark(background) ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.10f, 0.10f, 0.12f, 1f);

    /// <summary>
    /// Moves a colour away from its own end of the scale: lighter if it is
    /// dark, darker if it is light. Used for hover and pressed states, so they
    /// are visible on any theme.
    /// </summary>
    public static Vector4 Shift(Vector4 colour, float amount)
    {
        var target = IsDark(colour) ? new Vector4(1f, 1f, 1f, colour.W) : new Vector4(0f, 0f, 0f, colour.W);
        return Tint(colour, target, amount);
    }

    /// <summary>
    /// Pulls an accent toward the text colour until it is legible as text on
    /// the background. The game's default linkshell green, for one, all but
    /// vanishes on a light theme.
    /// </summary>
    public static Vector4 LegibleOn(Vector4 accent, Vector4 background, Vector4 text)
    {
        var difference = MathF.Abs(Luminance(accent) - Luminance(background));
        if (difference >= 0.35f) return accent;
        return Tint(accent, text, Math.Clamp((0.35f - difference) / 0.35f, 0f, 1f) * 0.75f);
    }

    /// <summary>
    /// A stable colour for a name, so the same person is the same colour every
    /// time without anything being stored. Saturation and brightness are fixed
    /// for the theme; only the hue varies.
    /// </summary>
    public static Vector4 ForName(string name, bool onDarkBackground)
    {
        var hash = 2166136261u;
        foreach (var ch in name)
        {
            hash ^= char.ToLowerInvariant(ch);
            hash *= 16777619u;
        }

        var hue = (hash % 360u) / 360f;
        return onDarkBackground ? FromHsv(hue, 0.42f, 0.98f) : FromHsv(hue, 0.78f, 0.58f);
    }

    public static Vector4 FromHsv(float hue, float saturation, float value, float alpha = 1f)
    {
        hue = (hue - MathF.Floor(hue)) * 6f;
        var sector = (int)hue;
        var fraction = hue - sector;
        var p = value * (1f - saturation);
        var q = value * (1f - (saturation * fraction));
        var t = value * (1f - (saturation * (1f - fraction)));

        return sector switch
        {
            0 => new Vector4(value, t, p, alpha),
            1 => new Vector4(q, value, p, alpha),
            2 => new Vector4(p, value, t, alpha),
            3 => new Vector4(p, q, value, alpha),
            4 => new Vector4(t, p, value, alpha),
            _ => new Vector4(value, p, q, alpha),
        };
    }

    private static uint ToByte(float channel) => (uint)Math.Clamp((int)MathF.Round(channel * 255f), 0, 255);
}
