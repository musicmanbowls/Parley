using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Parley.Core;

namespace Parley.Game;

/// <summary>
/// The game's auto-translate dictionary, read from its Completion sheet. Each
/// group starts with a heading row. The heading either says the group's
/// phrases follow it (<c>@</c>), or names another sheet and the rows of it to
/// use, such as <c>Mount[1-1,4-6]</c> or <c>Race</c> for every row. A phrase
/// from another sheet is worded the way Dalamud's auto-translate payload words
/// it, which is how the game shows it. Built group by group as asked for,
/// then kept. Framework thread only.
/// </summary>
internal static class AutoTranslate
{
    private const string OwnPhrases = "@";

    private static List<AutoTranslateGroup>? groups;
    private static readonly Dictionary<uint, string> Lookups = [];
    private static readonly Dictionary<uint, List<AutoTranslatePhrase>> Built = [];
    private static List<AutoTranslatePhrase>? everything;

    public static IReadOnlyList<AutoTranslateGroup> Groups()
    {
        if (groups != null) return groups;
        groups = [];
        try
        {
            foreach (var row in Services.Data.GetExcelSheet<Completion>())
            {
                var lookup = row.LookupTable.ExtractText().Trim();
                if (lookup.Length == 0 || Lookups.ContainsKey(row.Group)) continue;

                var title = row.GroupTitle.ExtractText().Trim().TrimEnd('.');
                if (title.Length == 0) continue;

                Lookups[row.Group] = lookup;
                groups.Add(new AutoTranslateGroup(row.Group, title));
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Could not read the auto-translate dictionary.");
        }
        return groups;
    }

    public static IReadOnlyList<AutoTranslatePhrase> Phrases(uint group)
    {
        if (Built.TryGetValue(group, out var known)) return known;

        var phrases = new List<AutoTranslatePhrase>();
        try
        {
            Groups();
            if (Lookups.TryGetValue(group, out var lookup))
            {
                if (lookup == OwnPhrases) AddOwn(group, phrases);
                else AddFromSheet(group, lookup, phrases);
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, $"Could not read auto-translate group {group}.");
        }

        Built[group] = phrases;
        return phrases;
    }

    /// <summary>Every phrase in every group, for searching. Built once, on the first search.</summary>
    public static IReadOnlyList<AutoTranslatePhrase> All()
    {
        if (everything != null) return everything;
        everything = [];
        foreach (var group in Groups()) everything.AddRange(Phrases(group.Id));
        return everything;
    }

    /// <summary>The phrases written into the Completion sheet itself, in the game's order.</summary>
    private static void AddOwn(uint group, List<AutoTranslatePhrase> phrases)
    {
        foreach (var row in Services.Data.GetExcelSheet<Completion>())
        {
            if (row.Group != group || row.LookupTable.ExtractText().Length > 0) continue;
            var text = row.Text.ExtractText().Trim();
            if (text.Length > 0) phrases.Add(new AutoTranslatePhrase(group, row.Key, text));
        }
    }

    /// <summary>The phrases taken from rows of another sheet, alphabetically.</summary>
    private static void AddFromSheet(uint group, string lookup, List<AutoTranslatePhrase> phrases)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Keys(lookup))
        {
            var text = Unbracket(new AutoTranslatePayload(group, key).Text);
            if (text.Length > 0 && seen.Add(text)) phrases.Add(new AutoTranslatePhrase(group, key, text));
        }
        phrases.Sort((a, b) => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The rows a heading names: the ranges in its brackets, or with none every row of that sheet.</summary>
    private static IEnumerable<uint> Keys(string lookup)
    {
        var bracket = lookup.IndexOf('[');
        if (bracket < 0)
        {
            foreach (var row in Services.Data.Excel.GetSheet<RawRow>(name: lookup)) yield return row.RowId;
            yield break;
        }

        foreach (var range in lookup[(bracket + 1)..].TrimEnd(']').Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = range.IndexOf('-');
            if (dash < 0)
            {
                if (uint.TryParse(range, out var single)) yield return single;
                continue;
            }

            if (!uint.TryParse(range[..dash], out var from) || !uint.TryParse(range[(dash + 1)..], out var to)) continue;
            for (var key = from; key <= to && key - from < 10_000; key++) yield return key;
        }
    }

    private static string Unbracket(string? text) =>
        (text ?? string.Empty).Trim((char)SeIconChar.AutoTranslateOpen, (char)SeIconChar.AutoTranslateClose, ' ');
}
