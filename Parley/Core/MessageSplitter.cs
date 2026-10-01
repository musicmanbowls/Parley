using System.Text;

namespace Parley.Core;

public readonly record struct SplitResult(IReadOnlyList<string> Parts, bool TooLong);

/// <summary>
/// Cuts a message that is too long for one chat line into several that fit.
///
/// The game's limit is on UTF-8 bytes of the whole line including the command
/// and tell target, so the budget is passed in by the caller, who knows that
/// prefix. Everything here measures bytes, not characters.
/// </summary>
public static class MessageSplitter
{
    /// <summary>More parts than this is treated as a paste accident rather than a message.</summary>
    public const int MaxParts = 8;

    /// <summary>A UTF-8 sequence is at most four bytes; a smaller budget cannot hold every character.</summary>
    private const int MinBudget = 4;

    /// <summary>Chat lines cannot contain line breaks or tabs; they become single spaces.</summary>
    public static string Normalise(string text)
    {
        if (text.AsSpan().IndexOfAny('\r', '\n', '\t') < 0) return text.Trim();

        var builder = new StringBuilder(text.Length);
        var lastWasBreak = false;
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n' or '\t')
            {
                if (!lastWasBreak) builder.Append(' ');
                lastWasBreak = true;
            }
            else
            {
                builder.Append(ch);
                lastWasBreak = false;
            }
        }
        return builder.ToString().Trim();
    }

    public static int ByteCount(string text) => Encoding.UTF8.GetByteCount(text);

    public static SplitResult Split(string text, int budgetBytes)
    {
        text = Normalise(text);
        var parts = new List<string>();
        if (text.Length == 0) return new SplitResult(parts, false);
        if (budgetBytes < MinBudget) return new SplitResult(parts, true);

        if (ByteCount(text) <= budgetBytes)
        {
            parts.Add(text);
            return new SplitResult(parts, false);
        }

        var current = new StringBuilder();
        var currentBytes = 0;

        void Flush()
        {
            if (current.Length == 0) return;
            parts.Add(current.ToString());
            current.Clear();
            currentBytes = 0;
        }

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var wordBytes = ByteCount(word);
            var needed = current.Length == 0 ? wordBytes : wordBytes + 1;
            if (currentBytes + needed <= budgetBytes)
            {
                if (current.Length > 0) current.Append(' ');
                current.Append(word);
                currentBytes += needed;
                continue;
            }

            Flush();
            if (wordBytes <= budgetBytes)
            {
                current.Append(word);
                currentBytes = wordBytes;
                continue;
            }

            // One unbroken run longer than a whole line, a pasted URL say. Cut
            // on rune boundaries so a surrogate pair is never split in half.
            foreach (var rune in word.EnumerateRunes())
            {
                if (currentBytes + rune.Utf8SequenceLength > budgetBytes) Flush();
                current.Append(rune.ToString());
                currentBytes += rune.Utf8SequenceLength;
            }
        }

        Flush();
        return new SplitResult(parts, parts.Count > MaxParts);
    }
}
