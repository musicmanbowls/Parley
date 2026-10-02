using System.Text;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.Text;
using Lumina.Excel.Sheets;

namespace Parley.Game;

/// <summary>
/// How the game's chat log writes a kind of line: the text around the sender
/// and the message, and its colour. That is either one of the game's global
/// numbers, counted from 1 as the game's text macros count them, or for a few
/// of the game's own kinds a fixed colour (0xRRGGBB).
/// </summary>
internal readonly record struct LineFormat(int ColourParameter, uint FixedColour, string Before, bool HasSender, string Between, string After)
{
    /// <summary>For a kind the game's data does not describe: "Name: message", in the default colour.</summary>
    public static readonly LineFormat Fallback = new(0, 0, string.Empty, true, ": ", string.Empty);
}

/// <summary>
/// Chat line formats, from the game's LogKind sheet, so General writes lines
/// the way the game's chat log does and in the game's language. A format such
/// as <c>&lt;color(gnum26)&gt;[FC]\&lt;&lt;string(lstr1)&gt;&gt; &lt;string(lstr2)&gt;&lt;color(stackcolor)&gt;</c>
/// is the colour, then text around the sender (lstr1) and message (lstr2).
///
/// The colour's global number holds whatever the player picked in Log Text
/// Colors, read the way Dalamud's own text evaluator reads it. Framework
/// thread only.
/// </summary>
internal static partial class LogFormats
{
    private static readonly Dictionary<int, LineFormat> Formats = [];

    [GeneratedRegex(@"^<color\((?:gnum(\d+)|(\d+))\)>(.*?)(?:<string\(lstr1\)>(.*?))?<string\(lstr2\)>(.*?)<color\(stackcolor\)>$", RegexOptions.Singleline)]
    private static partial Regex Shape();

    public static LineFormat For(int kind)
    {
        if (Formats.TryGetValue(kind, out var known)) return known;

        var format = LineFormat.Fallback;
        try
        {
            if (Services.Data.GetExcelSheet<LogKind>().TryGetRow((uint)kind, out var row))
            {
                var match = Shape().Match(row.Format.ToMacroString());
                if (match.Success)
                {
                    format = new LineFormat(
                        match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0,
                        match.Groups[2].Success ? (uint)(ulong.Parse(match.Groups[2].Value) & 0xFFFFFF) : 0,
                        Unescape(match.Groups[3].Value),
                        match.Groups[4].Success,
                        Unescape(match.Groups[4].Value),
                        Unescape(match.Groups[5].Value));
                }
            }
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, $"Could not read the chat format for line kind {kind}.");
        }

        Formats[kind] = format;
        return format;
    }

    /// <summary>The colour of a kind of line as 0xRRGGBB, or null when it has none Parley can read.</summary>
    public static unsafe uint? Colour(int kind)
    {
        var format = For(kind);
        if (format.FixedColour != 0) return format.FixedColour;

        // Counted from 1 in the macro, from 0 in the game's list, as Dalamud's evaluator does.
        var index = format.ColourParameter - 1;
        if (index < 0) return null;

        var module = RaptureTextModule.Instance();
        if (module == null) return null;

        ref var globals = ref module->TextModule.MacroDecoder.GlobalParameters;
        if ((ulong)index >= (ulong)globals.MySize) return null;

        var value = globals[index];
        if (value.Type != TextParameterType.Integer) return null;

        // Pushed as BGRA by the game's colour macro: 0xAARRGGBB as a number.
        var rgb = (uint)value.IntValue & 0xFFFFFF;
        return rgb == 0 ? null : rgb;
    }

    /// <summary>Macro text escapes a literal angle bracket with a backslash.</summary>
    private static string Unescape(string text)
    {
        if (!text.Contains('\\')) return text;
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length) i++;
            builder.Append(text[i]);
        }
        return builder.ToString();
    }
}
