using System.Text;

namespace Parley.Core;

/// <summary>Parsing and tidying of player names typed by hand.</summary>
public static class PlayerName
{
    /// <summary>
    /// Splits "First Last@World". Returns false when there is no world part,
    /// in which case <paramref name="name"/> still holds the tidied input.
    /// </summary>
    public static bool TrySplit(string input, out string name, out string world)
    {
        world = string.Empty;
        var text = input.Trim();
        var at = text.LastIndexOf('@');
        if (at <= 0 || at == text.Length - 1)
        {
            name = Tidy(at > 0 ? text[..at] : text);
            return false;
        }

        name = Tidy(text[..at]);
        world = text[(at + 1)..].Trim();
        return name.Length > 0 && world.Length > 0;
    }

    /// <summary>
    /// Whether a string could be a character name. Deliberately loose: naming
    /// rules differ between regions, and the game is the authority on whether
    /// the player exists. This only keeps out things that would change the
    /// meaning of the /tell line the name is pasted into.
    /// </summary>
    public static bool IsPlausible(string name)
    {
        if (name.Length is < 2 or > 32) return false;
        if (name[0] == '/') return false;
        foreach (var ch in name)
        {
            if (char.IsControl(ch) || ch is '@' or '<' or '>' or '"') return false;
        }
        return true;
    }

    /// <summary>Collapses runs of whitespace and capitalises the first letter of each part.</summary>
    public static string Tidy(string name)
    {
        var builder = new StringBuilder(name.Length);
        var startOfPart = true;
        foreach (var ch in name.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!startOfPart) builder.Append(' ');
                startOfPart = true;
                continue;
            }

            builder.Append(startOfPart ? char.ToUpperInvariant(ch) : ch);
            startOfPart = false;
        }
        return builder.ToString().TrimEnd();
    }

    /// <summary>One or two letters to stand in for a portrait: "Alice Smith" gives "AS".</summary>
    public static string Initials(string name)
    {
        Span<char> letters = stackalloc char[2];
        var count = 0;
        var startOfPart = true;
        foreach (var ch in name)
        {
            if (char.IsWhiteSpace(ch))
            {
                startOfPart = true;
                continue;
            }

            if (startOfPart && char.IsLetterOrDigit(ch))
            {
                letters[count++] = char.ToUpperInvariant(ch);
                if (count == 2) break;
            }
            startOfPart = false;
        }

        if (count == 1)
        {
            // A one-word name: borrow its second letter so the badge is not lopsided.
            var seenFirst = false;
            foreach (var ch in name)
            {
                if (!char.IsLetterOrDigit(ch)) continue;
                if (!seenFirst) { seenFirst = true; continue; }
                letters[count++] = char.ToLowerInvariant(ch);
                break;
            }
        }

        return count == 0 ? "?" : new string(letters[..count]);
    }
}
