using System.Text;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Utility;
using Parley.Core;

namespace Parley.Game;

/// <summary>
/// Items linked into a reply. In the reply box a link is a stand-in: the
/// game's link marker followed by the item's name, with its spaces made
/// non-breaking so it stays in one piece if a long message is split. When the
/// line goes to the game, each stand-in is swapped for a real item link, the
/// same bytes the game's own chat box puts in a line when you link an item.
/// </summary>
internal static class ItemLinks
{
    private const char Marker = '';

    private static readonly Dictionary<string, byte[]> Links = new(StringComparer.Ordinal);

    /// <summary>The stand-in for one item, registered so it can be swapped back when sent.</summary>
    public static string Token(uint rawId, string name)
    {
        var token = Marker + name.Trim().Replace(' ', ' ');
        if (Links.ContainsKey(token)) return token;

        try
        {
            var kind = ItemInfo.IsKeyItemId(rawId) ? ItemKind.EventItem
                : ItemInfo.IsHighQualityId(rawId) ? ItemKind.Hq
                : ItemInfo.IsCollectableId(rawId) ? ItemKind.Collectible
                : ItemKind.Normal;
            Links[token] = SeString.CreateItemLink(ItemInfo.BaseId(rawId), kind).Encode();
        }
        catch (Exception ex)
        {
            // Without the link the name still goes out, as plain text.
            Services.Log.Warning(ex, "Could not make an item link.");
        }
        return token;
    }

    /// <summary>
    /// The stand-in for an auto-translate phrase: the phrase between the game's
    /// auto-translate brackets, as the game shows one. It goes to the game as
    /// a real auto-translate phrase, which each reader sees in their own language.
    /// </summary>
    public static string Phrase(uint group, uint key, string text)
    {
        var token = PhraseOpen + text.Trim().Replace(' ', ' ') + PhraseClose;
        if (Links.ContainsKey(token)) return token;

        try
        {
            Links[token] = new AutoTranslatePayload(group, key).Encode();
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Could not make an auto-translate phrase.");
        }
        return token;
    }

    private const char PhraseOpen = (char)SeIconChar.AutoTranslateOpen;
    private const char PhraseClose = (char)SeIconChar.AutoTranslateClose;

    /// <summary>Whether a text might hold a stand-in at all, so most text skips the search.</summary>
    private static bool HasStandIns(string text) =>
        Links.Count > 0 && (text.IndexOf(Marker) >= 0 || text.IndexOf(PhraseOpen) >= 0);

    /// <summary>How many more bytes the stand-ins in a text take once they are links.</summary>
    public static int ExtraBytes(string text)
    {
        if (!HasStandIns(text)) return 0;

        var extra = 0;
        foreach (var (token, link) in Links)
        {
            var at = 0;
            while ((at = text.IndexOf(token, at, StringComparison.Ordinal)) >= 0)
            {
                extra += Math.Max(0, link.Length - Encoding.UTF8.GetByteCount(token));
                at += token.Length;
            }
        }
        return extra;
    }

    /// <summary>Runs <paramref name="clean"/> over the text between stand-ins, leaving the stand-ins themselves alone.</summary>
    public static string AroundTokens(string text, Func<string, string> clean)
    {
        if (!HasStandIns(text)) return clean(text);

        var builder = new StringBuilder(text.Length);
        var at = 0;
        while (at < text.Length)
        {
            var (start, token) = NextToken(text, at);
            if (token == null)
            {
                builder.Append(clean(text[at..]));
                break;
            }

            if (start > at) builder.Append(clean(text[at..start]));
            builder.Append(token);
            at = start + token.Length;
        }
        return builder.ToString();
    }

    /// <summary>The line as bytes for the game, with each stand-in replaced by its link.</summary>
    public static byte[] Encode(string line)
    {
        if (!HasStandIns(line)) return Encoding.UTF8.GetBytes(line);

        var bytes = new List<byte>(line.Length + 64);
        var at = 0;
        while (at < line.Length)
        {
            var (start, token) = NextToken(line, at);
            if (token == null)
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(line[at..]));
                break;
            }

            if (start > at) bytes.AddRange(Encoding.UTF8.GetBytes(line[at..start]));
            bytes.AddRange(Links[token]);
            at = start + token.Length;
        }
        return [.. bytes];
    }

    /// <summary>The first registered stand-in at or after a position, longest first where two start together.</summary>
    private static (int Start, string? Token) NextToken(string text, int from)
    {
        var best = -1;
        string? found = null;
        foreach (var token in Links.Keys)
        {
            var at = text.IndexOf(token, from, StringComparison.Ordinal);
            if (at < 0) continue;
            if (best < 0 || at < best || (at == best && token.Length > found!.Length))
            {
                best = at;
                found = token;
            }
        }
        return (best, found);
    }
}
