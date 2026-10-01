using System.Numerics;

namespace Parley.Core.Text;

/// <summary>
/// A message laid out at one width: where each character sits, where the
/// lines break, and the pieces to draw. Also answers the questions selecting
/// and clicking ask: which character is under a point, and which rectangles
/// cover a range.
///
/// Measuring is handed in, so this works the same against ImGui's fonts and
/// in tests. Lines break at spaces, after hyphens and slashes, and between
/// Japanese characters; a word wider than the whole line is cut wherever it
/// has to be.
/// </summary>
public sealed class TextLayout
{
    /// <summary>How far the pen moves for <paramref name="ch"/> following <paramref name="previous"/>, kerning included.</summary>
    public delegate float Measure(char ch, char previous);

    /// <param name="End">Exclusive. A line break character is in neither line.</param>
    /// <param name="Width">Up to the last character that is not a space.</param>
    public readonly record struct Line(int Start, int End, float Top, float Height, float Width);

    /// <summary>A piece to draw: one run's characters on one line, or one icon.</summary>
    public readonly record struct Segment(int Start, int End, int Line, int Run, bool IsObject);

    private TextLayout(RichText source, float[] x, float[] advance, Line[] lines, Segment[] segments, float width, float height)
    {
        Source = source;
        X = x;
        Advance = advance;
        Lines = lines;
        Segments = segments;
        Width = width;
        Height = height;
    }

    public RichText Source { get; }

    /// <summary>Left edge of each character, from the start of its line.</summary>
    public float[] X { get; }

    /// <summary>Width of each character. 0 for a line break and for the second half of a surrogate pair.</summary>
    public float[] Advance { get; }

    public Line[] Lines { get; }
    public Segment[] Segments { get; }

    /// <summary>The widest line, without trailing spaces.</summary>
    public float Width { get; }
    public float Height { get; }

    public static TextLayout Build(RichText source, float wrapWidth, float lineHeight, Measure measure, Func<int, Vector2>? objectSize = null)
    {
        var text = source.Text;
        var count = text.Length;
        var x = new float[count];
        var advance = new float[count];
        var lines = new List<Line>();
        var runs = source.Runs;

        var lineStart = 0;
        var pen = 0f;
        var breakAt = -1;
        var previous = '\0';
        var top = 0f;
        var run = 0;

        for (var i = 0; i < count; i++)
        {
            while (run < runs.Count - 1 && i >= runs[run].End) run++;
            var ch = text[i];

            if (ch == '\n')
            {
                x[i] = pen;
                advance[i] = 0f;
                EndLine(lineStart, i);
                lineStart = i + 1;
                pen = 0f;
                previous = '\0';
                breakAt = -1;
                continue;
            }

            float width;
            if (ch == RichText.ObjectChar && run < runs.Count && runs[run].Object >= 0)
                width = objectSize?.Invoke(runs[run].Object).X ?? lineHeight;
            else if (char.IsLowSurrogate(ch) && i > 0 && char.IsHighSurrogate(text[i - 1]))
                width = 0f;
            else
                width = measure(char.IsSurrogate(ch) ? '�' : ch, previous);

            if (pen + width > wrapWidth && i > lineStart && width > 0f && !IsSpace(ch))
            {
                var next = breakAt > lineStart ? breakAt : i;
                if (next < count && char.IsLowSurrogate(text[next]) && next > lineStart) next--;

                EndLine(lineStart, next);
                var shift = next < i ? x[next] : pen;
                for (var k = next; k < i; k++) x[k] -= shift;
                pen -= shift;
                lineStart = next;
                breakAt = -1;
            }

            x[i] = pen;
            advance[i] = width;
            pen += width;
            previous = ch;
            if (BreaksAfter(text, i)) breakAt = i + 1;
        }

        EndLine(lineStart, count);

        var widest = 0f;
        foreach (var line in lines) widest = MathF.Max(widest, line.Width);
        return new TextLayout(source, x, advance, [.. lines], BuildSegments(source, lines), widest, top);

        void EndLine(int start, int end)
        {
            var contentEnd = end;
            while (contentEnd > start && IsSpace(text[contentEnd - 1])) contentEnd--;
            var width = contentEnd > start ? x[contentEnd - 1] + advance[contentEnd - 1] : 0f;

            var height = lineHeight;
            for (var k = start; k < end; k++)
            {
                if (text[k] != RichText.ObjectChar || objectSize == null) continue;
                var at = source.RunAt(k);
                if (at >= 0 && runs[at].Object >= 0) height = MathF.Max(height, objectSize(runs[at].Object).Y);
            }

            lines.Add(new Line(start, end, top, height, width));
            top += height;
        }
    }

    private static Segment[] BuildSegments(RichText source, List<Line> lines)
    {
        var segments = new List<Segment>();
        var runs = source.Runs;
        var run = 0;
        for (var l = 0; l < lines.Count; l++)
        {
            var line = lines[l];
            while (run < runs.Count && runs[run].End <= line.Start) run++;
            for (var r = run; r < runs.Count && runs[r].Start < line.End; r++)
            {
                var start = Math.Max(line.Start, runs[r].Start);
                var end = Math.Min(line.End, runs[r].End);
                if (end > start) segments.Add(new Segment(start, end, l, r, runs[r].Object >= 0));
            }
        }
        return [.. segments];
    }

    /// <summary>The line at a height, clamped to the first or last.</summary>
    public int LineAt(float y)
    {
        for (var i = 0; i < Lines.Length; i++)
        {
            if (y < Lines[i].Top + Lines[i].Height) return i;
        }
        return Lines.Length - 1;
    }

    /// <summary>
    /// The position between characters nearest a point, for placing one end of
    /// a selection. Anywhere left of a line is its start, anywhere right of it
    /// its end.
    /// </summary>
    public int IndexAt(Vector2 point)
    {
        if (Lines.Length == 0) return 0;
        if (point.Y < 0f) return 0;
        if (point.Y >= Height) return Source.Text.Length;

        var line = Lines[LineAt(point.Y)];
        for (var k = line.Start; k < line.End; k++)
        {
            if (Advance[k] <= 0f) continue;
            if (point.X < X[k] + (Advance[k] * 0.5f)) return k;
        }
        return line.End;
    }

    /// <summary>The character whose box contains a point, or -1. For finding what is under the cursor.</summary>
    public int CharAt(Vector2 point)
    {
        if (Lines.Length == 0 || point.Y < 0f || point.Y >= Height) return -1;
        var line = Lines[LineAt(point.Y)];
        for (var k = line.Start; k < line.End; k++)
        {
            if (point.X >= X[k] && point.X < X[k] + Advance[k]) return k;
        }
        return -1;
    }

    /// <summary>Where a line's last character ends, trailing spaces included.</summary>
    public float LineEnd(int line)
    {
        var l = Lines[line];
        return l.End > l.Start ? X[l.End - 1] + Advance[l.End - 1] : 0f;
    }

    /// <summary>
    /// The rectangles that highlight characters <paramref name="start"/> to
    /// <paramref name="end"/>, one per line touched. A selection that carries
    /// on past a line's end is shown running a little past it.
    /// </summary>
    /// <param name="continues">The selection goes on into what follows this text.</param>
    /// <param name="tail">How far past the end of a line a selection that continues is drawn.</param>
    public void Highlight(int start, int end, bool continues, float tail, List<(Vector2 Min, Vector2 Max)> into)
    {
        for (var i = 0; i < Lines.Length; i++)
        {
            var line = Lines[i];
            if (end < line.Start || start > line.End) continue;

            var runsOn = end > line.End && (i < Lines.Length - 1 || continues);
            var from = Math.Max(start, line.Start);
            var to = Math.Min(end, line.End);
            if (to <= from && !runsOn) continue;

            var x0 = from < line.End ? X[from] : LineEnd(i);
            var x1 = to < line.End ? X[to] : LineEnd(i);
            if (runsOn) x1 += tail;
            if (x1 > x0) into.Add((new Vector2(x0, line.Top), new Vector2(x1, line.Top + line.Height)));
        }
    }

    /// <summary>Rectangles covering one link, for underlining it.</summary>
    public void Underline(TextLink link, List<(Vector2 Min, Vector2 Max)> into) => Highlight(link.Start, link.End, false, 0f, into);

    /// <summary>The word around a position: a run of letters and digits, of spaces, or of anything else.</summary>
    public static (int Start, int End) WordAt(string text, int index)
    {
        if (text.Length == 0) return (0, 0);
        index = Math.Clamp(index, 0, text.Length - 1);

        var kind = Kind(text[index]);
        var start = index;
        while (start > 0 && Kind(text[start - 1]) == kind) start--;
        var end = index + 1;
        while (end < text.Length && Kind(text[end]) == kind) end++;
        return (start, end);
    }

    private static int Kind(char ch) =>
        IsSpace(ch) ? 0 : char.IsLetterOrDigit(ch) || ch is '\'' or '_' || char.IsSurrogate(ch) ? 1 : 2;

    private static bool IsSpace(char ch) => ch is ' ' or '\t' or '　';

    private static bool BreaksAfter(string text, int i)
    {
        var ch = text[i];
        var next = i + 1 < text.Length ? text[i + 1] : '\0';
        if (IsSpace(ch)) return true;
        if (ch == RichText.ObjectChar || next == RichText.ObjectChar) return true;
        if (IsWide(ch) || IsWide(next)) return next != '\0' && !IsSpace(next);
        if (ch is '-' or '/' && next != '\0' && !IsSpace(next) && next != ch) return i > 0 && !IsSpace(text[i - 1]);
        return false;
    }

    /// <summary>Japanese, Chinese and Korean text, which breaks between any two characters.</summary>
    private static bool IsWide(char ch) =>
        ch is >= '぀' and <= 'ヿ' or >= '㐀' and <= '鿿' or >= '豈' and <= '﫿'
            or >= 'ｦ' and <= 'ﾟ' or >= '가' and <= '힯';
}
