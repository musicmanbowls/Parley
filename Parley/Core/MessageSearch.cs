namespace Parley.Core;

/// <summary>One message that matched a search.</summary>
/// <param name="Key">The conversation it is in.</param>
public sealed record SearchHit(string Key, long Timestamp, string Sender, string Text, bool Outgoing, int MatchStart, int MatchLength);

/// <summary>Matching messages against what was typed into the search box.</summary>
public static class MessageSearch
{
    /// <summary>Searches shorter than this find too much to be useful.</summary>
    public const int MinQueryLength = 2;

    /// <summary>Where the query first appears in the text, ignoring case, or -1.</summary>
    public static int Find(string text, string query) =>
        query.Length == 0 ? -1 : text.IndexOf(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>Searches messages already in memory, newest first, adding at most <paramref name="max"/> hits.</summary>
    public static void InMemory(Conversation conversation, string query, int max, List<SearchHit> into)
    {
        var messages = conversation.Messages;
        for (var i = messages.Count - 1; i >= 0 && into.Count < max; i--)
        {
            var message = messages[i];
            if (message.IsNotice) continue;
            var at = Find(message.Text, query);
            if (at < 0) continue;
            into.Add(new SearchHit(conversation.Key, message.Timestamp, message.Sender, message.Text, message.IsOutgoing, at, query.Length));
        }
    }

    /// <summary>
    /// A piece of the text around the match, short enough for one line of
    /// results, with where the match falls inside it.
    /// </summary>
    public static string Snippet(string text, int matchStart, int matchLength, int before, int total, out int snippetMatch)
    {
        matchStart = Math.Clamp(matchStart, 0, text.Length);
        matchLength = Math.Clamp(matchLength, 0, text.Length - matchStart);
        if (text.Length <= total)
        {
            snippetMatch = matchStart;
            return text;
        }

        var start = Math.Max(0, matchStart - before);
        if (start > 0)
        {
            // Begin at a word if there is one close by.
            var space = text.IndexOf(' ', start);
            if (space >= 0 && space < matchStart && space - start < 12) start = space + 1;
        }

        var length = Math.Min(total, text.Length - start);
        if (start > 0 && char.IsLowSurrogate(text[start])) start--;
        var end = start + length;
        if (end < text.Length && char.IsHighSurrogate(text[end - 1])) end--;

        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = end < text.Length ? "…" : string.Empty;
        snippetMatch = matchStart - start + prefix.Length;
        return prefix + text[start..end] + suffix;
    }
}
