namespace Parley.Core;

/// <summary>A game chat tab: its name, whether it is in use, and its table of the kinds of line it leaves out.</summary>
internal sealed record GameChatTab(int Index, string Name, bool InUse, byte[]? Hidden);

/// <summary>
/// The game's chat log as General shows it: the newest lines, oldest first,
/// and for each of the game's chat tabs the lines that tab shows. Kept for the
/// session only. Framework thread only.
///
/// Each line is decided against each tab's filter as it arrives. When the
/// player changes a filter in the game, every tab is worked out again, the way
/// the game's own tabs change what they show.
/// </summary>
internal sealed class GeneralLog
{
    public const int Capacity = 2000;

    private readonly List<ChatMessage> lines = [];
    private readonly List<ChatMessage>[] shown;
    private List<GameChatTab> tabs = [];

    public GeneralLog()
    {
        shown = new List<ChatMessage>[ChatLogFilter.TabCount];
        for (var i = 0; i < shown.Length; i++) shown[i] = [];
    }

    /// <summary>The game's tabs as last read. Empty until they have been.</summary>
    public IReadOnlyList<GameChatTab> Tabs => tabs;

    /// <summary>Every line kept, whichever tabs show it.</summary>
    public IReadOnlyList<ChatMessage> All => lines;

    /// <summary>Goes up whenever a line arrives or goes, or the tabs change.</summary>
    public int Revision { get; private set; }

    /// <summary>The lines a tab shows, oldest first.</summary>
    public IReadOnlyList<ChatMessage> Shown(int tab) => tab >= 0 && tab < shown.Length ? shown[tab] : [];

    public void Add(ChatMessage line)
    {
        lines.Add(line);
        for (var t = 0; t < shown.Length; t++)
        {
            if (Shows(t, line.LogInfo)) shown[t].Add(line);
        }

        if (lines.Count > Capacity)
        {
            var oldest = lines[0];
            lines.RemoveAt(0);
            foreach (var list in shown)
            {
                if (list.Count > 0 && ReferenceEquals(list[0], oldest)) list.RemoveAt(0);
            }
        }

        Revision++;
    }

    /// <summary>Takes the tabs as just read from the game, and works every tab out again if a filter changed.</summary>
    public void SetTabs(List<GameChatTab> read)
    {
        var filtersChanged = read.Count != tabs.Count;
        var namesChanged = filtersChanged;
        for (var i = 0; i < read.Count && i < tabs.Count; i++)
        {
            if (!SameTable(read[i].Hidden, tabs[i].Hidden)) filtersChanged = true;
            if (read[i].InUse != tabs[i].InUse || !string.Equals(read[i].Name, tabs[i].Name, StringComparison.Ordinal)) namesChanged = true;
        }

        if (!filtersChanged && !namesChanged) return;
        tabs = read;
        if (filtersChanged) Rebuild();
        Revision++;
    }

    public void Clear()
    {
        lines.Clear();
        foreach (var list in shown) list.Clear();
        Revision++;
    }

    private bool Shows(int tab, ushort info) => tab < tabs.Count && ChatLogFilter.Shows(tabs[tab].Hidden, info);

    private void Rebuild()
    {
        for (var t = 0; t < shown.Length; t++)
        {
            shown[t].Clear();
            foreach (var line in lines)
            {
                if (Shows(t, line.LogInfo)) shown[t].Add(line);
            }
        }
    }

    private static bool SameTable(byte[]? a, byte[]? b) =>
        ReferenceEquals(a, b) || (a != null && b != null && a.AsSpan().SequenceEqual(b));
}
