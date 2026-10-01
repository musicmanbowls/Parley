namespace Parley.Core.Text;

/// <summary>
/// Finds web addresses in chat text: anything starting http:// or https://,
/// anything starting www., and bare addresses such as youtu.be/abc or
/// example.com/page whose ending is a well-known domain.
///
/// Bare addresses are where the false alarms come from ("e.g.", "3.5",
/// "file.txt"), so they need a known domain ending, and a few endings that are
/// also everyday words ("to", "it", "is" and the like) are left out.
/// </summary>
public static class LinkFinder
{
    public readonly record struct Found(int Start, int Length, string Url);

    private static readonly HashSet<string> Endings = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "net", "org", "io", "gg", "tv", "be", "co", "me", "ly", "gl", "app", "dev", "info", "xyz",
        "uk", "ca", "au", "de", "fr", "jp", "eu", "nz", "ie", "es", "nl", "se", "fi", "dk", "pl", "br",
        "ru", "ch", "moe", "live", "link", "cc", "fm", "sh", "ai", "gov", "edu", "biz", "wiki", "social",
        "games", "online", "site", "store", "pro", "art", "blog", "page", "club", "team", "gay", "lol",
    };

    private const string Leading = "(<[{\"'";
    private const string Trailing = ".,;:!?)]}>\"'";

    /// <summary>Every address in <paramref name="text"/> between <paramref name="start"/> and start + length.</summary>
    public static List<Found> Find(string text, int start = 0, int length = -1)
    {
        var found = new List<Found>();
        var end = length < 0 ? text.Length : Math.Min(text.Length, start + length);

        var i = start;
        while (i < end)
        {
            while (i < end && IsSpace(text[i])) i++;
            var tokenStart = i;
            while (i < end && !IsSpace(text[i])) i++;
            if (i - tokenStart >= 4) Check(text, tokenStart, i, found);
        }

        return found;
    }

    /// <summary>The address for a stretch of text, or null if it is not one.</summary>
    public static string? AsUrl(string candidate)
    {
        var found = Find(candidate);
        return found.Count == 1 && found[0].Start == 0 && found[0].Length == candidate.Length ? found[0].Url : null;
    }

    private static void Check(string text, int start, int end, List<Found> found)
    {
        while (start < end && Leading.Contains(text[start])) start++;
        while (end > start && Trailing.Contains(text[end - 1]))
        {
            // A closing bracket that belongs to the address, as in a wiki
            // page's "(disambiguation)", stays.
            if (text[end - 1] == ')' && Count(text, start, end, '(') > Count(text, start, end - 1, ')')) break;
            end--;
        }
        if (end - start < 4) return;

        var token = text.AsSpan(start, end - start);
        string url;
        if (token.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || token.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = token.ToString();
        }
        else if (token.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + token.ToString();
        }
        else
        {
            if (!LooksLikeBareAddress(token)) return;
            url = "https://" + token.ToString();
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;
        if (uri.Host.Length == 0 || !uri.Host.Contains('.')) return;

        found.Add(new Found(start, end - start, url));
    }

    private static bool LooksLikeBareAddress(ReadOnlySpan<char> token)
    {
        if (token.Contains('@')) return false;

        // The host is everything up to the first path, query or port.
        var hostEnd = token.IndexOfAny("/?#:");
        var host = hostEnd < 0 ? token : token[..hostEnd];
        if (host.Length < 4) return false;

        var dot = host.LastIndexOf('.');
        if (dot <= 0 || dot == host.Length - 1) return false;
        if (!Endings.Contains(host[(dot + 1)..].ToString())) return false;

        var hasLetter = false;
        foreach (var label in host[..dot].ToString().Split('.'))
        {
            if (label.Length == 0) return false;
            foreach (var ch in label)
            {
                if (char.IsAsciiLetter(ch)) hasLetter = true;
                else if (!char.IsAsciiDigit(ch) && ch != '-') return false;
            }
        }
        return hasLetter;
    }

    private static int Count(string text, int start, int end, char what)
    {
        var count = 0;
        for (var i = start; i < end; i++)
        {
            if (text[i] == what) count++;
        }
        return count;
    }

    private static bool IsSpace(char ch) => char.IsWhiteSpace(ch) || ch == RichText.ObjectChar;
}
