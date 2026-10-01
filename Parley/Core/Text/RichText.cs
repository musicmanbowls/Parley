using System.Text;

namespace Parley.Core.Text;

/// <summary>What a stretch of a message does when it is clicked.</summary>
public enum LinkKind : byte
{
    None,

    /// <summary>A web address typed into the message.</summary>
    Url,

    /// <summary>An item linked from the game.</summary>
    Item,

    /// <summary>A map position, such as a flag.</summary>
    Map,

    /// <summary>A player's name, as "&lt;t&gt;" puts into a message.</summary>
    Player,

    /// <summary>A party finder listing.</summary>
    PartyFinder,

    /// <summary>Any other link the game makes: drawn in its colours, with nothing to do on a click.</summary>
    Other,
}

/// <summary>A clickable stretch of a message, from <see cref="Start"/> up to but not including <see cref="End"/>.</summary>
public sealed class TextLink
{
    public required LinkKind Kind { get; init; }
    public int Start { get; internal set; }
    public int End { get; internal set; }

    /// <summary>For a web link, the address to open, always http or https.</summary>
    public string? Url { get; init; }

    /// <summary>For an item, its id as the game links it (with the high-quality or collectable offset); for a listing, its id.</summary>
    public uint Id { get; init; }

    /// <summary>For a player, their name; for an item, its name when the link carries one.</summary>
    public string? Name { get; init; }

    /// <summary>For a player, their home world.</summary>
    public ushort World { get; init; }

    /// <summary>Whatever the game handed over for this link, such as a map position. Opaque here.</summary>
    public object? Payload { get; init; }
}

/// <summary>
/// One stretch of a message drawn the same way throughout.
/// </summary>
/// <param name="Colour">The game's UI colour number for it, or 0 for the message's own colour.</param>
/// <param name="Link">Index into <see cref="RichText.Links"/>, or -1.</param>
/// <param name="Object">Index into <see cref="RichText.Objects"/> for an inline icon, or -1. An icon run is one character long.</param>
public readonly record struct TextRun(int Start, int Length, ushort Colour, short Link, short Object)
{
    public int End => Start + Length;
}

/// <summary>
/// A message as text plus what is laid over it: colours, links and inline
/// icons. Built once per message and kept; layout is done separately, because
/// that depends on the width it is drawn at.
/// </summary>
public sealed class RichText
{
    /// <summary>Stands in for an inline icon in <see cref="Text"/>. Left out when copying.</summary>
    public const char ObjectChar = '￼';

    private readonly TextRun[] runs;
    private readonly TextLink[] links;
    private readonly byte[][] objects;

    private RichText(string text, TextRun[] runs, TextLink[] links, byte[][] objects)
    {
        Text = text;
        this.runs = runs;
        this.links = links;
        this.objects = objects;
    }

    public string Text { get; }
    public IReadOnlyList<TextRun> Runs => runs;
    public IReadOnlyList<TextLink> Links => links;

    /// <summary>Inline icons, as the encoded payloads the game sent. Drawing them is the UI's business.</summary>
    public IReadOnlyList<byte[]> Objects => objects;

    /// <summary>A plain message, with any web addresses in it made into links.</summary>
    public static RichText Plain(string text)
    {
        var builder = new Builder();
        builder.Append(text);
        return builder.Build();
    }

    /// <summary>The text between two positions as it should be copied: icons left out.</summary>
    public string Copy(int start, int end)
    {
        start = Math.Clamp(start, 0, Text.Length);
        end = Math.Clamp(end, start, Text.Length);
        var span = Text.AsSpan(start, end - start);
        if (span.IndexOf(ObjectChar) < 0) return span.ToString();

        var builder = new StringBuilder(span.Length);
        foreach (var ch in span)
        {
            if (ch != ObjectChar) builder.Append(ch);
        }
        return builder.ToString();
    }

    /// <summary>Which run the character at this position belongs to, or -1 past the end.</summary>
    public int RunAt(int index)
    {
        var low = 0;
        var high = runs.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) >> 1;
            var run = runs[middle];
            if (index < run.Start) high = middle - 1;
            else if (index >= run.End) low = middle + 1;
            else return middle;
        }
        return -1;
    }

    /// <summary>The link the character at this position is part of, if any.</summary>
    public TextLink? LinkAt(int index)
    {
        var run = RunAt(index);
        return run >= 0 && runs[run].Link >= 0 ? links[runs[run].Link] : null;
    }

    /// <summary>Puts a message together a piece at a time.</summary>
    public sealed class Builder
    {
        private readonly StringBuilder text = new();
        private readonly List<TextRun> runs = [];
        private readonly List<TextLink> links = [];
        private readonly List<byte[]> objects = [];
        private short openLink = -1;

        public int Length => text.Length;

        public void Append(string value, ushort colour = 0)
        {
            if (string.IsNullOrEmpty(value)) return;

            // An icon character arriving as text would be taken for one of ours.
            if (value.Contains(ObjectChar)) value = value.Replace(ObjectChar, ' ');

            var start = text.Length;
            text.Append(value);

            if (runs.Count > 0)
            {
                var last = runs[^1];
                if (last.End == start && last.Colour == colour && last.Link == openLink && last.Object < 0)
                {
                    runs[^1] = last with { Length = last.Length + value.Length };
                    return;
                }
            }
            runs.Add(new TextRun(start, value.Length, colour, openLink, -1));
        }

        /// <summary>An inline icon, kept as the bytes that describe it.</summary>
        public void AppendObject(byte[] data, ushort colour = 0)
        {
            if (objects.Count >= short.MaxValue) return;
            objects.Add(data);
            runs.Add(new TextRun(text.Length, 1, colour, openLink, (short)(objects.Count - 1)));
            text.Append(ObjectChar);
        }

        /// <summary>Everything appended from here until <see cref="EndLink"/> belongs to this link.</summary>
        public void BeginLink(TextLink link)
        {
            EndLink();
            if (links.Count >= short.MaxValue) return;
            link.Start = text.Length;
            link.End = text.Length;
            links.Add(link);
            openLink = (short)(links.Count - 1);
        }

        public void EndLink()
        {
            if (openLink < 0) return;
            links[openLink].End = text.Length;
            openLink = -1;
        }

        /// <param name="findUrls">Turn web addresses in the stretches that are not already links into links.</param>
        public RichText Build(bool findUrls = true)
        {
            EndLink();
            var built = text.ToString();
            var finalRuns = findUrls ? WithUrls(built) : runs;
            return new RichText(built, [.. finalRuns], [.. links], [.. objects]);
        }

        private List<TextRun> WithUrls(string built)
        {
            List<TextRun>? result = null;
            for (var r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                List<LinkFinder.Found>? found = null;
                if (run.Link < 0 && run.Object < 0 && run.Length >= 4) found = LinkFinder.Find(built, run.Start, run.Length);

                if (found == null || found.Count == 0)
                {
                    result?.Add(run);
                    continue;
                }

                result ??= runs.GetRange(0, r);
                var at = run.Start;
                foreach (var url in found)
                {
                    if (links.Count >= short.MaxValue) break;
                    if (url.Start > at) result.Add(run with { Start = at, Length = url.Start - at });

                    links.Add(new TextLink { Kind = LinkKind.Url, Url = url.Url, Start = url.Start, End = url.Start + url.Length });
                    result.Add(new TextRun(url.Start, url.Length, run.Colour, (short)(links.Count - 1), -1));
                    at = url.Start + url.Length;
                }

                if (at < run.End) result.Add(run with { Start = at, Length = run.End - at });
            }

            return result ?? runs;
        }
    }
}
