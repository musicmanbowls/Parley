namespace Parley.Core;

/// <summary>A group in the game's auto-translate dictionary, such as Greetings or Mounts.</summary>
internal sealed record AutoTranslateGroup(uint Id, string Title);

/// <summary>One auto-translate phrase: its group and key, which the game turns into the phrase in each reader's language, and its text in this one.</summary>
internal sealed record AutoTranslatePhrase(uint Group, uint Key, string Text);

/// <summary>Finding phrases by what was typed, as the auto-translate picker's search and Tab do.</summary>
internal static class AutoTranslateSearch
{
    /// <summary>
    /// Phrases containing the query, ignoring case: those that start with it
    /// first, then those with a word that does, then the rest, each in the
    /// order given. At most <paramref name="max"/>.
    /// </summary>
    public static List<AutoTranslatePhrase> Find(IEnumerable<AutoTranslatePhrase> phrases, string query, int max)
    {
        var found = new List<AutoTranslatePhrase>();
        query = query.Trim();
        if (query.Length == 0) return found;

        var starts = new List<AutoTranslatePhrase>();
        var wordStarts = new List<AutoTranslatePhrase>();
        var contains = new List<AutoTranslatePhrase>();
        foreach (var phrase in phrases)
        {
            var at = phrase.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            if (at == 0) starts.Add(phrase);
            else if (!char.IsLetterOrDigit(phrase.Text[at - 1])) wordStarts.Add(phrase);
            else contains.Add(phrase);
        }

        foreach (var list in new[] { starts, wordStarts, contains })
        {
            foreach (var phrase in list)
            {
                if (found.Count >= max) return found;
                found.Add(phrase);
            }
        }
        return found;
    }
}
