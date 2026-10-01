using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Ipc;
using Parley.Ipc;
using Umbra.Common;
using Umbra.Widgets;
using Una.Drawing;

namespace Umbra.Parley;

[ToolbarWidget(
    "Parley.Unread",
    "Parley",
    "Unread tells, free company, linkshell and cross-world linkshell messages. Left-click opens the Parley chat window; right-click marks everything as read.",
    ["chat", "tell", "fc", "free company", "linkshell", "cwls", "messages", "unread", "parley"])]
public sealed class ParleyWidget(WidgetInfo info, string? guid = null, Dictionary<string, object>? configValues = null)
    : StandardToolbarWidget(info, guid, configValues)
{
    /// <summary>How often Parley is asked for its status. The answer is cached on its side, so this is cheap.</summary>
    private const long PollMs = 250;

    private const string Settings = "Parley";
    private const string CountGroup = "Count";

    private const string CvarStyle = "ParleyStyle";
    private const string CvarCountTells = "ParleyCountTells";
    private const string CvarCountFreeCompany = "ParleyCountFreeCompany";
    private const string CvarCountLinkshells = "ParleyCountLinkshells";
    private const string CvarCountCrossWorld = "ParleyCountCrossWorld";
    private const string CvarIdleText = "ParleyIdleText";
    private const string CvarHideWhenIdle = "ParleyHideWhenIdle";
    private const string CvarNameSender = "ParleyNameSender";
    private const string CvarShowPreviews = "ParleyShowPreviews";
    private const string CvarLeftClick = "ParleyLeftClick";
    private const string CvarHighlight = "ParleyHighlight";
    private const string CvarHighlightColour = "ParleyHighlightColour";

    private const string ClickToggle = "Toggle";
    private const string ClickOpen = "Open";

    /// <summary>The game's default colour for tells, 0xAABBGGRR.</summary>
    private const uint TellPink = 0xFFDEB8FF;

    private ICallGateSubscriber<string>? statusGate;
    private ICallGateSubscriber<bool>? toggleGate;
    private ICallGateSubscriber<bool>? openGate;
    private ICallGateSubscriber<int>? markReadGate;
    private ParleyConnection? connection;
    private WidgetDisplay? display;
    private int shownGeneration = -1;
    private bool optionsChanged = true;
    private long nextPoll;

    protected override StandardWidgetFeatures Features =>
        StandardWidgetFeatures.Text | StandardWidgetFeatures.Icon | StandardWidgetFeatures.CustomizableIcon;

    protected override string DefaultIconType => IconTypeFontAwesome;
    protected override FontAwesomeIcon DefaultFontAwesomeIcon => FontAwesomeIcon.CommentDots;
    protected override int DefaultMaxTextWidth => 320;

    protected override IEnumerable<IWidgetConfigVariable> GetConfigVariables() =>
    [
        ..base.GetConfigVariables(),

        new SelectWidgetConfigVariable(CvarStyle, "What to show", "How unread messages are written on the button.", WidgetOptions.StylePhone, new()
        {
            [WidgetOptions.StylePhone] = "Notification: \"2 new Tells\"",
            [WidgetOptions.StyleCompact] = "Counters: \"T:2 LS:5 CW:1\"",
            [WidgetOptions.StyleTotal] = "One number: \"8 new messages\"",
        }) { Category = Settings },

        new BooleanWidgetConfigVariable(CvarCountTells, "Tells", "Count unread tells.", true)
            { Category = Settings, Group = CountGroup },
        new BooleanWidgetConfigVariable(CvarCountFreeCompany, "Free Company", "Count unread free company messages.", true)
            { Category = Settings, Group = CountGroup },
        new BooleanWidgetConfigVariable(CvarCountLinkshells, "Linkshells", "Count unread linkshell messages.", true)
            { Category = Settings, Group = CountGroup },
        new BooleanWidgetConfigVariable(CvarCountCrossWorld, "Cross-world linkshells", "Count unread cross-world linkshell messages.", true)
            { Category = Settings, Group = CountGroup },

        new StringWidgetConfigVariable(CvarIdleText, "Text when nothing is unread", "Shown on the button while there is nothing new.", "Chat", 32)
            { Category = Settings },
        new BooleanWidgetConfigVariable(CvarHideWhenIdle, "Hide when nothing is unread", "Take the button off the toolbar until a message arrives.", false)
            { Category = Settings },
        new BooleanWidgetConfigVariable(CvarNameSender, "Name the sender of a tell", "When every unread tell is from one person, show \"Tell from\" and their name instead of a count.", false)
            { Category = Settings, DisplayIf = () => GetConfigValue<string>(CvarStyle) == WidgetOptions.StylePhone },
        new BooleanWidgetConfigVariable(CvarShowPreviews, "Show messages in the tooltip", "List what each unread conversation last said when hovering the button. Turn off to show names and counts only.", true)
            { Category = Settings },

        new SelectWidgetConfigVariable(CvarLeftClick, "Left-click", "What a left-click on the button does. A right-click always marks everything as read.", ClickToggle, new()
        {
            [ClickToggle] = "Open the chat window, or close it if it is open",
            [ClickOpen] = "Always open the chat window",
        }) { Category = Settings },

        new BooleanWidgetConfigVariable(CvarHighlight, "Colour the text when there are unread messages", "Ignored while a custom text colour is set in the Text settings.", true)
            { Category = Settings },
        new ColorWidgetConfigVariable(CvarHighlightColour, "Unread text colour", "The colour of the button's text while there is something unread.", TellPink)
            { Category = Settings, DisplayIf = () => GetConfigValue<bool>(CvarHighlight) },
    ];

    protected override void OnLoad()
    {
        var pluginInterface = Framework.DalamudPlugin;
        statusGate = pluginInterface.GetIpcSubscriber<string>(ParleyIpc.GetStatus);
        toggleGate = pluginInterface.GetIpcSubscriber<bool>(ParleyIpc.ToggleWindow);
        openGate = pluginInterface.GetIpcSubscriber<bool>(ParleyIpc.OpenWindow);
        markReadGate = pluginInterface.GetIpcSubscriber<int>(ParleyIpc.MarkAllRead);

        // Asking first whether Parley has registered the gate avoids raising
        // and catching an exception four times a second while it is not loaded.
        connection = new ParleyConnection(() => statusGate is { HasFunction: true } ? statusGate.InvokeFunc() : null);

        Node.OnClick += OnLeftClick;
        Node.OnRightClick += OnRightClick;
        SetText("Parley");
        nextPoll = 0;
    }

    protected override void OnConfigurationChanged() => optionsChanged = true;

    protected override void OnDraw()
    {
        if (connection == null) return;

        var now = Environment.TickCount64;
        if (now >= nextPoll)
        {
            nextPoll = now + PollMs;
            connection.Refresh();
        }

        // The text and tooltip are only rebuilt when Parley's status or this
        // widget's settings have changed, not on every frame.
        if (display == null || optionsChanged || shownGeneration != connection.Generation)
        {
            optionsChanged = false;
            shownGeneration = connection.Generation;
            display = WidgetPresentation.Create(connection.Snapshot, ReadOptions(), connection.UnavailableReason);
            SetText(display.Text);
            SetTooltip(display.Tooltip);
            IsVisible = display.Visible;
        }

        // The base class resets the text colour every frame, so the highlight
        // has to be put back every frame too.
        if (display.Unread > 0 && !CvarUseCustomTextColor() && GetConfigValue<bool>(CvarHighlight))
            SetTextColor(new Color(GetConfigValue<uint>(CvarHighlightColour)), null);
    }

    protected override void OnUnload()
    {
        Node.OnClick -= OnLeftClick;
        Node.OnRightClick -= OnRightClick;
        connection = null;
        display = null;
        statusGate = null;
        toggleGate = null;
        openGate = null;
        markReadGate = null;
    }

    private WidgetOptions ReadOptions() => new()
    {
        Style = GetConfigValue<string>(CvarStyle),
        CountTells = GetConfigValue<bool>(CvarCountTells),
        CountFreeCompany = GetConfigValue<bool>(CvarCountFreeCompany),
        CountLinkshells = GetConfigValue<bool>(CvarCountLinkshells),
        CountCrossWorld = GetConfigValue<bool>(CvarCountCrossWorld),
        IdleText = GetConfigValue<string>(CvarIdleText),
        HideWhenIdle = GetConfigValue<bool>(CvarHideWhenIdle),
        NameSingleSender = GetConfigValue<bool>(CvarNameSender),
        ShowPreviews = GetConfigValue<bool>(CvarShowPreviews),
        ToggleOnClick = GetConfigValue<string>(CvarLeftClick) != ClickOpen,
    };

    private void OnLeftClick(Node _)
    {
        if (IsSettingsShortcut()) return;

        var gate = GetConfigValue<string>(CvarLeftClick) == ClickOpen ? openGate : toggleGate;
        Call(gate);
    }

    private void OnRightClick(Node _)
    {
        if (IsSettingsShortcut()) return;

        Call(markReadGate);
    }

    /// <summary>
    /// Umbra opens a widget's settings on Ctrl+Shift+right-click. A click with
    /// those two held is meant for Umbra, not for this button.
    /// </summary>
    private static bool IsSettingsShortcut()
    {
        var io = ImGui.GetIO();
        return io.KeyCtrl && io.KeyShift;
    }

    private void Call<T>(ICallGateSubscriber<T>? gate)
    {
        try
        {
            if (gate is { HasFunction: true }) gate.InvokeFunc();
        }
        catch (Exception e)
        {
            // Parley went away between the check and the call. The tooltip
            // will say so on the next poll; there is nothing else to do here.
            Logger.Warning($"Parley did not answer: {e.Message}");
        }

        // Show the result now rather than up to a quarter of a second later.
        nextPoll = 0;
    }
}
