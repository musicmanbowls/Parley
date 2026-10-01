using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Parley.Core;
using Parley.Core.Settings;
using Parley.Core.Theme;

namespace Parley.Ui;

/// <summary>
/// The chat window: a tab per kind of conversation along the top, the
/// conversations of that kind down the side, and the selected one in the
/// middle with a box to reply in.
///
/// The same class is also a popped-out window for one kind of conversation
/// on its own, the free company say, with no tabs. Each window keeps its own
/// selection, scroll position and search.
///
/// Split across several files by region of the window. This one holds the
/// window's lifetime, what is selected, and the order things are drawn in.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private const string WindowId = "###ParleyMain";

    /// <summary>The window itself never scrolls; the parts inside it that need to look after their own.</summary>
    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    /// <summary>If the window has not drawn for this long it is not on screen, whatever <see cref="Window.IsOpen"/> says.</summary>
    private const long OnScreenMs = 250;

    private readonly Plugin plugin;
    private readonly ConversationStore store;
    private readonly Configuration config;
    private readonly ThemeManager theme;
    private readonly FontManager fonts;

    /// <summary>For a popped-out window, the one kind of conversation it shows. Null for the main window.</summary>
    private readonly ChannelGroup? only;

    // Refreshed at the top of every Draw; valid only while drawing.
    private Palette palette;
    private float scale = 1f;
    private long now;

    /// <summary>Today as a day number in local time, for telling "today" from "yesterday" without converting per row.</summary>
    private int today;

    private ChannelGroup group;
    private readonly string?[] selectedKeys = new string?[ChannelGroups.Count];
    private Conversation? selected;

    /// <summary>
    /// The conversation whose reply box should take the keyboard next time it
    /// is drawn, or <see cref="WhicheverIsShown"/>. Tied to a conversation
    /// because each has a box of its own, and a click that selects one lands
    /// part way through a frame that is still drawing the previous one.
    /// </summary>
    private string? focusComposer;

    private const string WhicheverIsShown = "\0";

    /// <summary>Whether this frame shows the list of conversations. The free company tab does without it when there is only the one.</summary>
    private bool showSidebar = true;

    /// <summary>
    /// Set when the window was opened by something other than the user, such
    /// as a tell arriving. It then appears without taking focus, so whatever
    /// opened it still counts as unread until the user turns to it.
    /// </summary>
    private bool openQuietly;
    private long lastDrawn = -OnScreenMs;
    private long lastDrawError = -5000;
    private int titleUnread = -1;

    public MainWindow(Plugin plugin, ChannelGroup? only = null)
        : base(only is { } popped ? $"{popped.Label()}###Parley{popped}" : "Parley" + WindowId, BaseFlags)
    {
        this.plugin = plugin;
        this.only = only;
        store = plugin.Store;
        config = plugin.Config;
        theme = plugin.Theme;
        fonts = plugin.Fonts;
        palette = theme.Palette;
        group = only ?? config.LastGroup;
        Viewer = only is { } kind ? 1 + (int)kind : 0;
        placeCaret = PlaceCaret;

        Size = only == null ? new Vector2(780, 520) : new Vector2(620, 440);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(only == null ? 540 : 420, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(2, 1),
            Click = _ => plugin.OpenSettings(),
            ShowTooltip = () => ImGui.SetTooltip("Parley settings"),
        });
    }

    /// <summary>The conversation currently shown, if the window has one selected.</summary>
    public Conversation? Selected => selected;

    /// <summary>Which slot this window uses to tell the store what it is showing.</summary>
    public int Viewer { get; }

    /// <summary>For a popped-out window, the kind of conversation it shows.</summary>
    public ChannelGroup? PoppedGroup => only;

    /// <summary>The tab the window is on.</summary>
    public ChannelGroup CurrentGroup => group;

    /// <summary>Whether the window is actually being drawn, as opposed to open but hidden with the rest of the UI.</summary>
    public bool IsOnScreen => IsOpen && Environment.TickCount64 - lastDrawn < OnScreenMs;

    /// <summary>Whether some message text is highlighted, for Ctrl+C to copy.</summary>
    public bool HasSelection => !selection.IsEmpty;

    // Shortcuts the plugin picked up from the game's keyboard while this
    // window had focus but nothing in it was being typed in; see
    // Plugin.HandleWindowKeys. Acted on at the next draw.
    private bool copyRequested;
    private bool searchRequested;
    private int cycleRequested;

    /// <summary>Whether Enter has somewhere to go in this window: a reply box that can be written in, or the search box. As of the last draw.</summary>
    public bool AcceptsEnter => searching || canReply;

    /// <summary>Set while drawing: the reply box on screen can be written in.</summary>
    private bool canReply;

    /// <summary>
    /// Enter was pressed with this window the chat in use: puts the cursor in
    /// its reply box (or the search box, if that is open), as Enter does with
    /// the game's own chat.
    /// </summary>
    public void StartTyping()
    {
        if (!IsOpen) return;
        BringToFront();
        if (searching)
        {
            focusSearch = true;
            focusSearchFrames = 0;
        }
        else
        {
            focusComposer = WhicheverIsShown;
        }
    }

    public void RequestCopy() => copyRequested = true;
    public void RequestSearch() => searchRequested = true;
    public void RequestCycle(int step) => cycleRequested = step;

    /// <summary>Whether this window shows a kind of conversation: a popped-out window its own, the main window the rest.</summary>
    public bool Shows(ChannelGroup kind) => only is { } popped ? kind == popped : !config.IsPoppedOut(kind);

    /// <summary>
    /// Opens the window. With no conversation given, it goes to the most
    /// recent unread one if that option is on, and otherwise stays where it was.
    /// </summary>
    /// <param name="focus">False when nobody asked for this, as when a tell opens it: the keyboard stays with the game.</param>
    public void Show(Conversation? conversation = null, bool focus = true)
    {
        if (conversation != null)
        {
            if (!Shows(conversation.Group))
            {
                plugin.ShowConversation(conversation, focus);
                return;
            }
            Select(conversation, focus);
        }
        else
        {
            var unread = only is { } kind ? store.NewestUnread(kind) : NewestUnreadShown();
            if (!IsOpen && config.JumpToUnreadOnOpen && unread != null) Select(unread, focus);
            else focusComposer = focus && config.FocusInputOnOpen ? WhicheverIsShown : null;
        }

        if (!IsOpen) openQuietly = !focus;
        IsOpen = true;
        if (focus) BringToFront();
    }

    /// <summary>
    /// Closes the window if it is open, otherwise opens it the way
    /// <see cref="Show"/> does. Named apart from the base class's Toggle,
    /// which flips <see cref="Window.IsOpen"/> and nothing else.
    /// </summary>
    /// <returns>True if the window ended up open.</returns>
    public bool ToggleOpen()
    {
        if (IsOpen)
        {
            IsOpen = false;
            return false;
        }

        Show();
        return true;
    }

    /// <summary>The conversation this window has selected for a kind, for handing over when a tab is popped out or put back.</summary>
    public string? SelectedKey(ChannelGroup kind) => selectedKeys[(int)kind];

    public void SelectKey(ChannelGroup kind, string? key)
    {
        if (key != null) selectedKeys[(int)kind] = key;
    }

    public override void OnOpen()
    {
        // Reopening starts the list afresh: at the first unread message if
        // there are any, otherwise at the newest, wherever it had been
        // scrolled to when the window was closed.
        viewConversation = null;
    }

    public override void OnClose()
    {
        if (selected != null) Leave(selected);
        selected = null;
        store.SetViewed(Viewer, null);
        Game.GameLinks.HideItemTooltip(this);
        CloseSearch();
    }

    public override void PreDraw()
    {
        fonts.Apply(config);
        theme.Resolve();
        theme.Push();

        var opacity = config.WindowOpacity;
        if (theme.Themed) BgAlpha = theme.Palette.WindowBg.W * opacity;
        else BgAlpha = opacity < 0.999f ? opacity : null;

        // Escape closing a popped-out window would only put it away; it is
        // the main window that Escape is for.
        RespectCloseHotkey = config.CloseWithEscape;

        // ImGui gives a window focus as it appears unless told not to.
        Flags = openQuietly ? BaseFlags | ImGuiWindowFlags.NoFocusOnAppearing : BaseFlags;

        var unread = UnreadShown();
        if (unread != titleUnread)
        {
            titleUnread = unread;
            var name = only is { } kind ? kind.Label() : "Parley";
            var id = only is { } popped ? $"###Parley{popped}" : WindowId;
            WindowName = unread > 0 ? $"{name} ({unread}){id}" : name + id;
        }
    }

    public override void PostDraw() => theme.Pop();

    public override void Draw()
    {
        // The window has appeared by now, which is the only moment the
        // request not to take focus applies to.
        openQuietly = false;

        lastDrawn = Environment.TickCount64;
        canReply = false;
        palette = theme.Palette;
        scale = ImGuiHelpers.GlobalScale;
        now = store.Now;
        today = (int)(TimeText.ToLocal(now).Date.Ticks / TimeSpan.TicksPerDay);

        using var font = fonts.Push();

        // Dalamud replaces a window's contents with an error panel for good
        // the first time its Draw throws. A chat window is worth more than
        // that, so a failed frame is logged and the next one tries again.
        // Every region below unwinds what it pushed on to ImGui if it fails.
        try
        {
            RefreshLayoutStamp();
            KeepGroupShown();
            HandleShortcuts();
            DrawGroupTabs();
            ResolveSelection();

            if (!store.HasCharacter)
            {
                DrawCentred("Log in to see your conversations.", ImGui.GetContentRegionAvail());
            }
            else if (!Shows(group))
            {
                DrawCentred("Every kind of conversation has a window of its own.\nClick a tab above to bring one up.", ImGui.GetContentRegionAvail());
            }
            else if (searching)
            {
                DrawSearch(ImGui.GetContentRegionAvail().Y);
            }
            else
            {
                // A character is in one free company at most, so that tab
                // only needs a list when there are old ones to choose from.
                showSidebar = group != ChannelGroup.FreeCompany || store.InGroup(group).Count > 1;

                var height = ImGui.GetContentRegionAvail().Y;
                if (showSidebar)
                {
                    DrawSidebar(height);
                    ImGui.SameLine(0f, 0f);
                    DrawSplitter(height);
                    ImGui.SameLine(0f, 0f);
                }
                DrawPane(height);
            }

            DrawNewTellPopup();
            DrawConfirmPopup();
            DrawNameColourPopup();
        }
        catch (Exception ex)
        {
            if (lastDrawn - lastDrawError > 5000)
            {
                lastDrawError = lastDrawn;
                Services.Log.Error(ex, "Could not draw the chat window.");
            }
        }

        EndItemTooltip();
        ReleaseKeyboardIfAsked();

        // A conversation is being read while the window has the user's
        // attention: focus, or the cursor over it. A window merely left open
        // beside the game does not swallow new messages; they count as unread
        // until the user comes back to it.
        var reading = selected != null && !searching && (config.ReadWhenVisible || IsFocused || IsHovered);
        store.SetViewed(Viewer, reading ? selected!.Key : null);
        if (reading && selected!.Unread > 0) store.MarkRead(selected);
    }

    // ------------------------------------------------------------------
    // Selection
    // ------------------------------------------------------------------

    private void Select(Conversation conversation, bool focus)
    {
        store.Reopen(conversation);
        SwitchGroup(conversation.Group);
        selectedKeys[(int)group] = conversation.Key;
        focusComposer = focus && config.FocusInputOnOpen ? conversation.Key : null;
        if (searching) CloseSearch();
    }

    /// <summary>
    /// Keyboard shortcuts that belong to the window as a whole. Only while it
    /// has focus. Most of the time they reach the window through the plugin
    /// (see <see cref="RequestCopy"/>); ImGui only sees the keys itself while
    /// a text box is being typed in.
    /// </summary>
    private void HandleShortcuts()
    {
        if (copyRequested)
        {
            copyRequested = false;
            CopySelection();
        }

        if (searchRequested)
        {
            searchRequested = false;
            OpenSearch();
        }

        if (cycleRequested != 0)
        {
            var step = cycleRequested;
            cycleRequested = 0;
            Cycle(step);
        }

        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) return;

        var io = ImGui.GetIO();

        // Ctrl+C copies highlighted message text. While a text box is being
        // typed in, the box has Ctrl+C for itself.
        if (io.KeyCtrl && !io.KeyAlt && !io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.C, false)) CopySelection();

        if (io.KeyCtrl && !io.KeyAlt && ImGui.IsKeyPressed(ImGuiKey.F, false)) OpenSearch();

        // Alt+R steps through the conversations in the open tab, Alt+Shift+R
        // back again. Plugin keeps the game from seeing the same key press.
        if (!config.CycleWithAltR) return;
        if (!io.KeyAlt || io.KeyCtrl || !ImGui.IsKeyPressed(ImGuiKey.R, false)) return;
        Cycle(io.KeyShift ? -1 : 1);
    }

    /// <summary>Moves the selection along the open tab's list, wrapping round at either end.</summary>
    private void Cycle(int step)
    {
        if (!store.HasCharacter || !Shows(group)) return;

        var list = store.InGroup(group);
        if (list.Count == 0) return;

        var at = -1;
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], selected)) at = i;
        }

        var next = at < 0 ? 0 : (at + step + list.Count) % list.Count;
        Select(list[next], focus: true);
    }

    private void SwitchGroup(ChannelGroup next)
    {
        if (group == next || only != null) return;
        group = next;
        config.LastGroup = next;
        plugin.SaveConfig();
    }

    /// <summary>The main window moves off a tab that has just been popped out into a window of its own.</summary>
    private void KeepGroupShown()
    {
        if (only != null || Shows(group)) return;
        foreach (var kind in ChannelGroups.All)
        {
            if (!Shows(kind)) continue;
            SwitchGroup(kind);
            return;
        }
    }

    /// <summary>
    /// Turns the remembered key for this tab into a conversation, falling back
    /// to the first in the list when the remembered one has gone.
    /// </summary>
    private void ResolveSelection()
    {
        Conversation? next = null;
        if (store.HasCharacter && Shows(group))
        {
            var key = selectedKeys[(int)group];
            if (key != null) next = store.Find(key);
            if (next is { Closed: true } || (next != null && next.Group != group)) next = null;

            if (next == null)
            {
                var list = store.InGroup(group);
                next = list.Count > 0 ? list[0] : null;
                selectedKeys[(int)group] = next?.Key;
            }
        }

        if (ReferenceEquals(next, selected)) return;
        if (selected != null) Leave(selected);
        selected = next;
    }

    /// <summary>Tidies a conversation that is no longer the one on screen.</summary>
    private void Leave(Conversation conversation)
    {
        store.ClearUnreadMarker(conversation);
        store.Trim(conversation);
    }

    /// <summary>Unread messages in the kinds of conversation this window shows.</summary>
    private int UnreadShown()
    {
        if (only is { } kind) return store.Unread(kind);

        var total = 0;
        foreach (var each in ChannelGroups.All)
        {
            if (Shows(each)) total += store.Unread(each);
        }
        return total;
    }

    private Conversation? NewestUnreadShown()
    {
        Conversation? best = null;
        foreach (var kind in ChannelGroups.All)
        {
            if (!Shows(kind) || store.NewestUnread(kind) is not { } candidate) continue;
            if (best == null || candidate.LastActivity > best.LastActivity) best = candidate;
        }
        return best;
    }

    // ------------------------------------------------------------------
    // Shared drawing
    // ------------------------------------------------------------------

    private void DrawCentred(string text, Vector2 area)
    {
        var origin = ImGui.GetCursorScreenPos();
        var wrap = MathF.Max(60f * scale, area.X - (40f * scale));
        var size = ImGui.CalcTextSize(text, false, wrap);
        ImGui.GetWindowDrawList().AddText(
            ImGui.GetFont(), ImGui.GetFontSize(),
            new Vector2(origin.X + MathF.Max(0f, (area.X - size.X) * 0.5f), origin.Y + MathF.Max(0f, (area.Y - size.Y) * 0.4f)),
            Painter.U32(palette.TextMuted), text, wrap);
        if (area.X > 0f && area.Y > 0f) ImGui.Dummy(area);
    }
}
