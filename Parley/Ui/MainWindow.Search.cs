using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core;
using Parley.Core.Theme;

namespace Parley.Ui;

/// <summary>
/// Searching messages: the open conversation, the open tab, or everything,
/// through the whole saved history and not just what is loaded. Picking a
/// result opens its conversation at that message, paging in older history
/// until it gets there, and briefly lights the message up.
/// </summary>
internal sealed partial class MainWindow
{
    private enum SearchScope
    {
        Conversation,
        Tab,
        Everything,
    }

    private const int MaxHits = 300;
    private const long SearchDelayMs = 250;
    private const long FlashMs = 2200;

    private bool searching;
    private bool focusSearch;
    private int focusSearchFrames;
    private string searchQuery = string.Empty;
    private SearchScope searchScope = SearchScope.Tab;
    private List<SearchHit>? searchHits;
    private string searchedFor = string.Empty;
    private SearchScope searchedIn;
    private bool searchRunning;
    private long searchDue;
    private int searchToken;

    // Going to a message once its conversation is open.
    private string? seekKey;
    private long seekTimestamp;
    private string? seekText;
    private ChatMessage? flashMessage;
    private long flashUntil;

    /// <summary>Opens the search box, or puts the cursor back in it if it is already open.</summary>
    private void OpenSearch()
    {
        if (!store.HasCharacter) return;
        searching = true;
        focusSearch = true;
        focusSearchFrames = 0;
        if (selected == null && searchScope == SearchScope.Conversation) searchScope = SearchScope.Tab;
    }

    private void CloseSearch()
    {
        searching = false;
        searchToken++;
        searchRunning = false;
    }

    private void DrawSearch(float height)
    {
        var style = ImGui.GetStyle();
        var buttonSize = ImGui.GetFrameHeight();
        var scopeWidth = 170f * scale;

        // Asked for again each frame until it takes: a request made while
        // Ctrl is still down from Ctrl+F is not honoured.
        if (focusSearch && ++focusSearchFrames <= 30) ImGui.SetKeyboardFocusHere();

        ImGui.SetNextItemWidth(MathF.Max(60f * scale, ImGui.GetContentRegionAvail().X - scopeWidth - buttonSize - (style.ItemSpacing.X * 2f)));
        if (ImGui.InputTextWithHint("##search", "Search messages", ref searchQuery, 200)) searchDue = now + SearchDelayMs;
        if (ImGui.IsItemActive())
        {
            focusSearch = false;
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) CloseSearch();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(scopeWidth);
        if (ImGui.BeginCombo("##scope", ScopeLabel(searchScope)))
        {
            try
            {
                foreach (var scope in Enum.GetValues<SearchScope>())
                {
                    if (scope == SearchScope.Conversation && selected == null) continue;
                    if (ImGui.Selectable(ScopeLabel(scope), scope == searchScope) && scope != searchScope)
                    {
                        searchScope = scope;
                        searchDue = now;
                    }
                }
            }
            finally
            {
                ImGui.EndCombo();
            }
        }

        ImGui.SameLine();
        if (Painter.IconButton("##closesearch", FontAwesomeIcon.Times, "Close the search", palette, buttonSize)) CloseSearch();
        if (!searching) return;

        RunSearchIfDue();

        var query = searchQuery.Trim();
        string status;
        if (query.Length < MessageSearch.MinQueryLength) status = "Type at least two letters.";
        else if (searchRunning || searchHits == null) status = "Searching…";
        else if (searchHits.Count == 0) status = "Nothing found.";
        else if (searchHits.Count >= MaxHits) status = $"The newest {MaxHits} matches.";
        else status = searchHits.Count == 1 ? "1 match." : $"{searchHits.Count} matches.";
        ImGui.TextDisabled(status);

        var visible = ImGui.BeginChild("##hits", new Vector2(0f, MathF.Max(20f * scale, ImGui.GetContentRegionAvail().Y)), false);
        try
        {
            if (visible && searchHits != null && query.Length >= MessageSearch.MinQueryLength)
            {
                var width = ImGui.GetContentRegionAvail().X;
                for (var i = 0; i < searchHits.Count; i++) DrawHit(searchHits[i], i, width);
            }
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private string ScopeLabel(SearchScope scope) => scope switch
    {
        SearchScope.Conversation => selected != null ? $"In {Painter.Fit(selected.Title, 120f * scale)}" : "This conversation",
        SearchScope.Tab => $"All {group.Label()}",
        _ => "Everything",
    };

    private void RunSearchIfDue()
    {
        var query = searchQuery.Trim();
        if (query.Length < MessageSearch.MinQueryLength)
        {
            searchHits = null;
            searchedFor = string.Empty;
            return;
        }

        var stale = !string.Equals(query, searchedFor, StringComparison.Ordinal) || searchScope != searchedIn;
        if (!stale || now < searchDue) return;

        searchedFor = query;
        searchedIn = searchScope;
        searchRunning = true;
        var token = ++searchToken;

        IEnumerable<Conversation> scope = searchScope switch
        {
            SearchScope.Conversation when selected != null => [selected],
            SearchScope.Everything => store.All.ToList(),
            _ => store.All.Where(conversation => conversation.Group == group).ToList(),
        };

        store.Search(scope, query, MaxHits, hits =>
        {
            if (token != searchToken) return;
            searchHits = hits;
            searchRunning = false;
        });
    }

    /// <summary>One result: where and when, then who said it with the match picked out.</summary>
    private void DrawHit(SearchHit hit, int index, float width)
    {
        var conversation = store.Find(hit.Key);
        var lineHeight = ImGui.GetTextLineHeight();
        var rowHeight = (lineHeight * 2f) + (10f * scale);
        var min = ImGui.GetCursorScreenPos();
        var max = new Vector2(min.X + width, min.Y + rowHeight);

        ImGui.PushID(index);
        if (ImGui.InvisibleButton("##hit", new Vector2(width, rowHeight)) && conversation != null) OpenHit(conversation, hit);
        var hovered = ImGui.IsItemHovered();
        ImGui.PopID();

        var drawList = ImGui.GetWindowDrawList();
        if (hovered) drawList.AddRectFilled(min, max, Painter.U32(palette.RowHover), 6f * scale);

        var left = min.X + (8f * scale);
        var top = min.Y + (5f * scale);
        var title = conversation?.Title ?? "A conversation that has gone";
        var when = TimeText.Sidebar(hit.Timestamp, now, config.Use24Hour);
        var whenWidth = ImGui.CalcTextSize(when).X;
        var titleColour = conversation != null ? ColourMath.LegibleOn(theme.ColourFor(conversation), palette.WindowBg, palette.Text) : palette.TextMuted;
        drawList.AddText(new Vector2(left, top), Painter.U32(titleColour), Painter.Fit(title, width - whenWidth - (24f * scale)));
        drawList.AddText(new Vector2(max.X - whenWidth - (8f * scale), top), Painter.U32(palette.TextMuted), when);

        // "Name: …text…" with the matching part highlighted.
        var speaker = hit.Outgoing ? "You" : hit.Sender.Length > 0 ? FirstName(hit.Sender) : string.Empty;
        var prefix = speaker.Length > 0 ? speaker + ": " : string.Empty;
        var room = width - (16f * scale) - ImGui.CalcTextSize(prefix).X;
        var chars = Math.Max(16, (int)(room / MathF.Max(1f, ImGui.CalcTextSize("e").X)));
        var snippet = MessageSearch.Snippet(hit.Text, hit.MatchStart, hit.MatchLength, chars / 3, chars, out var at);
        snippet = Painter.Fit(snippet, room);

        var y = top + lineHeight + (2f * scale);
        drawList.AddText(new Vector2(left, y), Painter.U32(palette.TextMuted), prefix);
        var x = left + ImGui.CalcTextSize(prefix).X;
        if (at >= 0 && at < snippet.Length)
        {
            var end = Math.Min(snippet.Length, at + hit.MatchLength);
            var start = x + ImGui.CalcTextSize(snippet.AsSpan(0, at)).X;
            var stop = x + ImGui.CalcTextSize(snippet.AsSpan(0, end)).X;
            drawList.AddRectFilled(new Vector2(start - 1f, y), new Vector2(stop + 1f, y + lineHeight), Painter.U32(ColourMath.WithAlpha(palette.Accent, 0.35f)), 3f * scale);
        }
        drawList.AddText(new Vector2(x, y), Painter.U32(palette.Text), snippet);
    }

    private static string FirstName(string name)
    {
        var space = name.IndexOf(' ');
        return space > 0 ? name[..space] : name;
    }

    /// <summary>Opens the conversation a result is in and goes to the message.</summary>
    private void OpenHit(Conversation conversation, SearchHit hit)
    {
        CloseSearch();
        plugin.ShowConversation(conversation);
        plugin.WindowFor(conversation.Group).SeekTo(conversation, hit.Timestamp, hit.Text);
    }

    /// <summary>Asks the message list to go to a message, paging in history until it is loaded.</summary>
    public void SeekTo(Conversation conversation, long timestamp, string? text)
    {
        seekKey = conversation.Key;
        seekTimestamp = timestamp;
        seekText = text;
        searching = false;
    }

    /// <summary>
    /// Each frame while a message is being looked for: scrolls to it if it is
    /// loaded, asks for an older page if it is not, gives up at the start.
    /// </summary>
    private void SeekMessage(Conversation conversation, float width, float viewHeight, float total)
    {
        if (seekKey == null) return;
        if (!string.Equals(seekKey, conversation.Key, StringComparison.OrdinalIgnoreCase))
        {
            seekKey = null;
            return;
        }

        var messages = conversation.Messages;
        var found = -1;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var message = messages[i];
            if (message.Timestamp < seekTimestamp) break;
            if (message.Timestamp == seekTimestamp && (seekText == null || string.Equals(message.Text, seekText, StringComparison.Ordinal)))
            {
                found = i;
                break;
            }
        }

        if (found < 0)
        {
            if (conversation.History == HistoryState.Loading) return;
            if (!conversation.ReachedStart && (messages.Count == 0 || messages[0].Timestamp >= seekTimestamp) && store.LoadOlder(conversation)) return;
            seekKey = null;
            return;
        }

        // How far down the list the message starts: everything above it, and the "load earlier" row.
        var metrics = BuildMetrics(width);
        var top = !conversation.ReachedStart ? ImGui.GetFrameHeight() + (10f * scale) : 0f;
        for (var i = 0; i < found; i++) top += Describe(conversation, i, metrics).Height;

        scroll.ShowAtTop(MathF.Max(0f, top - (viewHeight * 0.3f)), total, viewHeight);
        flashMessage = messages[found];
        flashUntil = Environment.TickCount64 + FlashMs;
        seekKey = null;
        seekUnread = false;
    }

    /// <summary>A fading highlight behind the message a search result led to.</summary>
    private void DrawFlash(ImDrawListPtr drawList, float left, float y, float width, in Metrics metrics, in Block block)
    {
        var remaining = flashUntil - Environment.TickCount64;
        if (remaining <= 0)
        {
            flashMessage = null;
            return;
        }

        var height = (block.Header ? metrics.HeaderHeight : 0f) + block.TextSize.Y + (metrics.PadY * 2f);
        var alpha = 0.35f * MathF.Min(1f, remaining / (FlashMs * 0.5f));
        drawList.AddRectFilled(new Vector2(left, y - (2f * scale)), new Vector2(left + width, y + height + (2f * scale)),
            Painter.U32(ColourMath.WithAlpha(palette.Accent, alpha)), 6f * scale);
    }
}
