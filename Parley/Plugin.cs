using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Parley.Core;
using Parley.Core.History;
using Parley.Core.Settings;
using Parley.Core.Theme;
using Parley.Game;
using Parley.Integration;
using Parley.Ui;

namespace Parley;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/parley";

    /// <summary>How often the game's linkshell lists are read. They change when the player joins, leaves or reorders.</summary>
    private const long SlotRefreshMs = 2000;

    /// <summary>Settings are written this long after the last change, so dragging a slider is one write.</summary>
    private const long ConfigDebounceMs = 750;

    /// <summary>
    /// How many refreshes in a row a linkshell list must come back empty
    /// before that is believed. One empty read while the client is between
    /// zones should not mark every linkshell as left.
    /// </summary>
    private const int EmptyListPatience = 3;

    private const int ToastPreviewLength = 120;

    /// <summary>A busy channel sounds once in this long, however many messages arrive.</summary>
    private const long SoundGapMs = 1500;

    /// <summary>And shows at most one notification per conversation in this long.</summary>
    private const long ToastGapMs = 4000;

    /// <summary>How often General reads the game's chat tabs, to follow changes to their names and filters.</summary>
    private const long GeneralTabsReadMs = 1000;

    /// <summary>How long a chat log colour is used before it is read from the game again.</summary>
    private const long ChatColourRefreshMs = 2000;

    private readonly string configPath;
    private readonly WindowSystem windows = new("Parley");
    private readonly int[] emptyStreak = new int[ChannelGroups.Count];
    private readonly bool[] slotsSeen = new bool[ChannelGroups.Count];
    private readonly long[] lastSound = new long[ChannelGroups.Count];
    private readonly Dictionary<string, long> lastToast = [];
    private readonly AlertPlayer alerts = new();

    private ChatCapture? capture;
    private DtrEntry? dtr;
    private ContextMenuIntegration? contextMenu;
    private SettingsWindow? settingsWindow;

    private ulong contentId;
    private long nextSlotRefresh;
    private long nextGeneralTabsRead;

    /// <summary>Whether General has been put on screen since logging in, while Parley stands in for the game's chat.</summary>
    private bool generalShownThisLogin;

    /// <summary>Whether Parley was standing in for the game's chat last tick, to notice it starting or stopping.</summary>
    private bool wasReplacing;

    /// <summary>When the game's chat box took the keyboard, while Parley stands in for it. Zero when it has not.</summary>
    private long gameInputSince;

    /// <summary>How long to leave a key that opened the game's chat box to land in it, before taking what was typed.</summary>
    private const long GameInputSettleMs = 40;

    private TellRequests? tellRequests;

    private readonly Dictionary<int, Vector4?> chatColours = [];
    private long chatColoursRead;

    // Whether each of the windows' shortcut keys was taken from the game last
    // tick, so a held key acts once and not on every tick it is down.
    private bool copyKeyHeld;
    private bool findKeyHeld;
    private bool cycleKeyHeld;

    /// <summary>The tell on screen last tick, so opening a different one can fetch the friend list at once.</summary>
    private string? lastTellOnScreen;

    private bool enterKeyHeld;
    private bool altEnterHeld;
    private bool mouseWasDown;

    /// <summary>The chat window being used, if any: the one Enter puts the cursor in. See HandleWindowKeys.</summary>
    private MainWindow? chatInUse;
    private long configDirtySince;

    /// <summary>The settings file being written on a worker, if any. Each write waits for the one before.</summary>
    private Task configWrite = Task.CompletedTask;
    private long lastErrorLogged;
    private bool disposed;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();

        var directory = pluginInterface.GetPluginConfigDirectory();
        configPath = Path.Combine(directory, "config.json");

        Config = ConfigurationFile.Load(configPath, LogIoError);
        History = new HistoryStore(Path.Combine(directory, "history"), LogIoError);

        try
        {
            Worlds = new WorldLookup(Services.Data);
            Linkshells = new LinkshellDirectory();
            Store = new ConversationStore(History) { ResolveWorld = Worlds.Id };
            Outgoing = new OutgoingQueue(ChatSender.TrySend) { Sanitise = ChatSender.Sanitise, ExtraBytes = ItemLinks.ExtraBytes };
            Outgoing.Failed += OnSendFailed;

            GameColours = new GameColours(Services.GameConfig);
            Theme = new ThemeManager(Config, GameColours);
            Fonts = new FontManager();

            MainWindow = new MainWindow(this);
            settingsWindow = new SettingsWindow(this);
            windows.AddWindow(MainWindow);
            foreach (var group in ChannelGroups.All)
            {
                var popOut = new MainWindow(this, group);
                PopOuts[(int)group] = popOut;
                windows.AddWindow(popOut);
            }
            windows.AddWindow(settingsWindow);

            Ipc = new IpcProvider(this);
            dtr = new DtrEntry(this);
            contextMenu = new ContextMenuIntegration(this);
            capture = new ChatCapture(this);
            tellRequests = new TellRequests();

            ApplyConfig();

            Services.Commands.AddHandler(Command, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open or close the Parley chat window.\n"
                              + $"{Command} First Last@World → open a tell with that player\n"
                              + $"{Command} <part of a name> → open an existing conversation\n"
                              + $"{Command} read → mark everything as read\n"
                              + $"{Command} friends → check what Parley can see of your friend list\n"
                              + $"{Command} tabs → check what Parley reads of the game's chat tabs\n"
                              + $"{Command} config → open the settings",
            });

            var builder = pluginInterface.UiBuilder;
            builder.Draw += windows.Draw;
            builder.OpenMainUi += OpenMainUi;
            builder.OpenConfigUi += OpenSettings;
            Services.Framework.Update += OnFrameworkUpdate;
        }
        catch
        {
            // Dalamud does not call Dispose on a plugin whose constructor
            // threw, and half of the above is subscriptions that would
            // otherwise outlive it.
            Dispose();
            throw;
        }
    }

    internal Configuration Config { get; }
    internal HistoryStore History { get; }
    internal ConversationStore Store { get; } = null!;
    internal OutgoingQueue Outgoing { get; } = null!;
    internal WorldLookup Worlds { get; } = null!;
    internal LinkshellDirectory Linkshells { get; } = null!;
    internal GameColours GameColours { get; } = null!;
    internal ThemeManager Theme { get; } = null!;
    internal FontManager Fonts { get; } = null!;
    internal MainWindow MainWindow { get; } = null!;

    /// <summary>One window per kind of conversation, open only while that kind is popped out of the main window.</summary>
    internal MainWindow[] PopOuts { get; } = new MainWindow[ChannelGroups.Count];

    internal IpcProvider Ipc { get; } = null!;

    /// <summary>Where history files live, for the settings window to show and open.</summary>
    internal string HistoryDirectory => History.Root;

    /// <summary>The logged-in character's name. Empty at the title screen.</summary>
    internal string LocalName { get; private set; } = string.Empty;

    /// <summary>The logged-in character's home world.</summary>
    internal ushort LocalWorldId { get; private set; }

    /// <summary>The world the character is on right now, which differs from home while visiting.</summary>
    internal ushort CurrentWorldId
    {
        get
        {
            var world = Services.PlayerState.IsLoaded ? Services.PlayerState.CurrentWorld.RowId : 0;
            return world is > 0 and <= ushort.MaxValue ? (ushort)world : LocalWorldId;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        Services.Framework.Update -= OnFrameworkUpdate;

        // The game's chat log must never stay hidden after Parley has gone.
        // Dalamud disposes plugins on the framework thread, where it is safe to touch.
        try
        {
            GameChatWindow.Restore();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not show the game's chat log again.");
        }

        var builder = Services.PluginInterface.UiBuilder;
        builder.Draw -= windows.Draw;
        builder.OpenMainUi -= OpenMainUi;
        builder.OpenConfigUi -= OpenSettings;
        Services.Commands.RemoveHandler(Command);

        capture?.Dispose();
        tellRequests?.Dispose();
        contextMenu?.Dispose();
        dtr?.Dispose();
        Ipc?.Dispose();
        windows.RemoveAllWindows();
        Fonts?.Dispose();
        alerts.Dispose();

        if (Outgoing != null) Outgoing.Failed -= OnSendFailed;

        // Unloading writes the index; the history store then gets a moment to
        // put that and any queued messages on disk.
        Store?.UnloadCharacter();
        History.Dispose();

        if (configDirtySince != 0) WriteConfig(wait: true);
        else configWrite.Wait(5000);
    }

    // ------------------------------------------------------------------
    // Things the UI and integrations ask for
    // ------------------------------------------------------------------

    /// <returns>True if the window ended up open.</returns>
    internal bool ToggleWindow() => MainWindow.ToggleOpen();

    internal bool OpenWindow()
    {
        MainWindow.Show();
        return true;
    }

    internal void OpenSettings()
    {
        if (settingsWindow == null) return;
        settingsWindow.IsOpen = true;
        settingsWindow.BringToFront();
    }

    /// <summary>Opens the chat window on a tell with this player, starting the conversation if there is none.</summary>
    internal void OpenTell(string name, ushort worldId)
    {
        if (!Store.HasCharacter) return;
        var conversation = Store.OpenTell(name, worldId, Worlds.Name(worldId));
        ShowConversation(conversation);
    }

    /// <summary>Every chat window: the main one and each popped-out one.</summary>
    internal IEnumerable<MainWindow> ChatWindows
    {
        get
        {
            yield return MainWindow;
            foreach (var popOut in PopOuts)
            {
                if (popOut != null) yield return popOut;
            }
        }
    }

    /// <summary>The window a kind of conversation is shown in: its own if it is popped out, otherwise the main one.</summary>
    internal MainWindow WindowFor(ChannelGroup group) => Config.IsPoppedOut(group) ? PopOuts[(int)group] : MainWindow;

    internal void ShowConversation(Conversation conversation, bool focus = true) => WindowFor(conversation.Group).Show(conversation, focus);

    internal void ShowPopOut(ChannelGroup group)
    {
        if (!Config.IsPoppedOut(group)) return;
        PopOuts[(int)group].Show();
    }

    /// <summary>Gives a kind of conversation a window of its own, carrying over what the main window had selected.</summary>
    internal void PopOut(ChannelGroup group)
    {
        Config.SetPoppedOut(group, true);
        SaveConfig();
        var window = PopOuts[(int)group];
        window.SelectKey(group, MainWindow.SelectedKey(group));
        window.Show();
    }

    /// <summary>Puts a popped-out kind of conversation back as a tab of the main window, which then shows it.</summary>
    internal void DockBack(ChannelGroup group)
    {
        var window = PopOuts[(int)group];
        MainWindow.SelectKey(group, window.SelectedKey(group));
        Config.SetPoppedOut(group, false);
        SaveConfig();
        window.IsOpen = false;

        var selected = window.SelectedKey(group) is { } key ? Store.Find(key) : null;
        if (selected != null) MainWindow.Show(selected);
        else if (MainWindow.IsOpen) MainWindow.Show();
    }

    /// <summary>Opens a web address in the default browser. Only ever http or https.</summary>
    internal void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
        try
        {
            Dalamud.Utility.Util.OpenLink(uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Could not open a link.");
        }
    }

    /// <summary>What the friend list says about someone, when that is turned on.</summary>
    internal FriendStatus FriendStatus(string name, ushort worldId) =>
        Config.ShowFriendStatus && Store.HasCharacter ? FriendList.Status(name, worldId, Worlds) : Core.FriendStatus.None;

    /// <summary>The stand-in for an item linked into a reply; see <see cref="ItemLinks"/>.</summary>
    internal string ItemLinkToken(uint rawId, string name) => ItemLinks.Token(rawId, name);

    // The game's auto-translate dictionary, for the picker beside each reply box.
    internal IReadOnlyList<AutoTranslateGroup> AutoTranslateGroups() => AutoTranslate.Groups();
    internal IReadOnlyList<AutoTranslatePhrase> AutoTranslatePhrases(uint group) => AutoTranslate.Phrases(group);
    internal IReadOnlyList<AutoTranslatePhrase> AutoTranslateAll() => AutoTranslate.All();

    /// <summary>The stand-in for an auto-translate phrase in a reply, sent as the real thing.</summary>
    internal string AutoTranslateToken(AutoTranslatePhrase phrase) => ItemLinks.Phrase(phrase.Group, phrase.Key, phrase.Text);

    internal SendOutcome Send(Conversation conversation, string text) =>
        Store.HasCharacter ? Outgoing.Send(conversation, text) : SendOutcome.CannotSend;

    // ------------------------------------------------------------------
    // General: the game's own chat log
    // ------------------------------------------------------------------

    /// <summary>The game's chat log, tab by tab, while General is turned on.</summary>
    internal GeneralLog General { get; } = new();

    /// <summary>
    /// The channel the game's chat box is on, as Parley names it: null when
    /// Parley does not know it, in which case the game's label is all there is.
    /// For a tell, also who it goes to.
    /// </summary>
    internal (ChatChannel? Channel, string Label, string TellTo) CurrentChannel()
    {
        var (id, label, tellTo) = GameChatTabs.CurrentChannel();
        return (id == ChatChannels.TellId ? null : ChatChannels.ById(id), label, id == ChatChannels.TellId ? tellTo : string.Empty);
    }

    /// <summary>The game's chat log colour for a kind of line, from Log Text Colors. Null when the game gives none.</summary>
    internal Vector4? ChatColour(int kind)
    {
        var now = Environment.TickCount64;
        if (now - chatColoursRead > ChatColourRefreshMs)
        {
            chatColoursRead = now;
            chatColours.Clear();
        }

        if (chatColours.TryGetValue(kind, out var known)) return known;

        Vector4? colour = null;
        try
        {
            if (LogFormats.Colour(kind) is { } rgb) colour = ColourMath.FromRgb(rgb);
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, $"Could not read the colour for line kind {kind}.");
        }

        chatColours[kind] = colour;
        return colour;
    }

    /// <summary>Types a line into the game's chat box, as though in the game's own: it goes to the box's channel, and commands work.</summary>
    internal bool SendGeneral(string text)
    {
        if (!Store.HasCharacter) return false;
        var line = ChatSender.Sanitise(text.Trim());
        return line.Length > 0 && ChatSender.TrySend(line);
    }

    /// <summary>Whether Parley is standing in for the game's chat log right now.</summary>
    internal bool ReplacingGameChat => Config.ReplaceGameChat && Config.GeneralChat && Store.HasCharacter;

    /// <summary>
    /// While Parley stands in for the game's chat log: keeps the game's chat
    /// windows hidden, puts General on screen once per login, and takes the
    /// keyboard over from the game's chat box whenever the game puts it there.
    /// Otherwise makes sure the game's chat log is back.
    /// </summary>
    private void UpdateGameChat(long tick)
    {
        var replacing = ReplacingGameChat;
        if (replacing != wasReplacing)
        {
            wasReplacing = replacing;
            ApplyUiHiding();
        }

        if (!replacing)
        {
            GameChatWindow.Restore();
            gameInputSince = 0;
            return;
        }

        GameChatWindow.Hide();

        if (!generalShownThisLogin)
        {
            generalShownThisLogin = true;
            MainWindow.TypeInGeneral(focus: false);
        }

        if (!GameChatWindow.InputActive())
        {
            gameInputSince = 0;
            return;
        }

        // The game has put the keyboard in its own, hidden, chat box. A moment
        // later, so a "/" from its slash key has landed in it, whatever is
        // there comes across to Parley's box, and with it a tell the game was
        // asked to start, as Send Tell does.
        if (gameInputSince == 0)
        {
            gameInputSince = tick;
            return;
        }
        if (tick - gameInputSince < GameInputSettleMs) return;
        gameInputSince = 0;

        var typed = GameChatWindow.TakeInput();
        GameChatWindow.ReleaseInput();

        var prefill = typed;
        if (tellRequests?.Take() is { } tell)
        {
            var world = tell.World.Length > 0 ? tell.World : Worlds.Name(tell.WorldId);
            prefill = world.Length > 0 ? $"/tell {tell.Name}@{world} " : $"/tell {tell.Name} ";
        }
        MainWindow.TypeInGeneral(prefill: prefill);
    }

    /// <summary>
    /// Whether the game's own chat log would be out of sight right now: in a
    /// cutscene, group pose, a loading screen, or with the game's UI hidden.
    /// While Parley stands in for it, Parley goes too, until Enter calls it up.
    /// </summary>
    internal bool GameChatWouldHide()
    {
        var condition = Services.Condition;
        if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51] || Services.ClientState.IsGPosing || Services.GameGui.GameUiHidden)
            return true;

        // Some cutscenes leave the chat log up, and the game marks a cutscene
        // as started a moment before it hides anything. Its own flag decides.
        var cutscene = condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.WatchingCutscene] || condition[ConditionFlag.WatchingCutscene78];
        return cutscene && GameChatWindow.HiddenByGame();
    }

    /// <summary>Opens the game's Log Window Settings, for General's cog.</summary>
    internal bool OpenGameLogSettings()
    {
        try
        {
            return GameChatWindow.OpenLogSettings();
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Could not open the game's Log Window Settings.");
            return false;
        }
    }

    /// <summary>Has the game add a chat tab, as its own "+" does, for General's "+". The new tab shows up in General when the game has made it.</summary>
    internal bool AddGameChatTab()
    {
        try
        {
            if (!GameChatWindow.AddTab()) return false;
            nextGeneralTabsRead = 0;
            return true;
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Could not add a tab to the game's chat log.");
            return false;
        }
    }

    /// <summary>
    /// Which of Dalamud's reasons to hide plugin windows apply to Parley's. While
    /// Parley stands in for the game's chat it decides for itself, so that Enter
    /// can still bring it up in a cutscene, as it does the game's chat.
    /// </summary>
    private void ApplyUiHiding()
    {
        var builder = Services.PluginInterface.UiBuilder;
        var replacing = ReplacingGameChat;
        builder.DisableCutsceneUiHide = replacing || Config.ShowInCutscenes;
        builder.DisableGposeUiHide = replacing || Config.ShowInGpose;
        builder.DisableUserUiHide = replacing || Config.ShowWhenUiHidden;
    }

    // ------------------------------------------------------------------
    // What has been sent, for the up arrow
    // ------------------------------------------------------------------

    private const int SentHistoryLimit = 100;

    /// <summary>Lines sent from any of Parley's boxes this session, oldest first, as the up arrow brings them back.</summary>
    internal List<string> SentHistory { get; } = [];

    internal void RecordSent(string text)
    {
        var line = text.Trim();
        if (line.Length == 0) return;
        if (SentHistory.Count > 0 && string.Equals(SentHistory[^1], line, StringComparison.Ordinal)) return;
        SentHistory.Add(line);
        if (SentHistory.Count > SentHistoryLimit) SentHistory.RemoveAt(0);
    }

    /// <summary>Reads the game's chat tabs for General, or lets go of its lines once it is turned off.</summary>
    private void UpdateGeneral(long tick)
    {
        if (!Config.GeneralChat || !Store.HasCharacter)
        {
            if (General.All.Count > 0) General.Clear();
            return;
        }

        if (tick < nextGeneralTabsRead) return;
        nextGeneralTabsRead = tick + GeneralTabsReadMs;
        try
        {
            General.SetTabs(GameChatTabs.Read());
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, "Could not read the game's chat tabs.");
        }
    }

    /// <summary>Marks the settings as changed. They are applied now and written shortly.</summary>
    internal void SaveConfig()
    {
        if (configDirtySince == 0) configDirtySince = Environment.TickCount64;
    }

    /// <summary>Pushes settings that live outside the configuration object to where they take effect.</summary>
    internal void ApplyConfig()
    {
        Store.PageSize = Config.PageSize;
        Store.MaxLoadedMessages = Config.MaxLoadedMessages;
        Outgoing.SplitLongMessages = Config.SplitLongMessages;
        Outgoing.SplitDelayMs = Config.SplitDelayMs;
        ApplyUiHiding();
    }

    /// <summary>
    /// Drops the loaded character so the next tick loads it afresh. Used when
    /// a setting that is fixed per session, such as whether history is saved,
    /// has been changed.
    /// </summary>
    internal void ReloadCharacter()
    {
        Store.UnloadCharacter();
        contentId = 0;
    }

    /// <summary>Plays a kind of conversation's sound, for the settings window's Test button.</summary>
    /// <returns>Why nothing played, or null.</returns>
    internal string? TestAlert(ChannelGroup group) => PlayAlert(Config.Alert(group));

    /// <summary>Reads the game's linkshell and free company lists and tells the store which is in which slot.</summary>
    internal void RefreshLinkshells()
    {
        if (!Store.HasCharacter) return;

        Linkshells.Refresh();
        ApplySlots(ChannelGroup.Linkshell, Linkshells.Local);
        ApplySlots(ChannelGroup.CrossWorld, Linkshells.Cross);
        ApplySlots(ChannelGroup.FreeCompany, Linkshells.FreeCompany);
    }

    /// <summary>Called for every message that arrives from someone else, in any kind of conversation.</summary>
    internal void OnIncoming(Conversation conversation, ChatMessage message)
    {
        if (conversation.Muted) return;

        // A conversation being read right now needs no sound or notification.
        var reading = Store.IsViewed(conversation.Key);
        var alert = Config.Alert(conversation.Group);
        var now = Environment.TickCount64;
        var group = (int)conversation.Group;

        if (!reading && alert.Sound != AlertSound.None && now - lastSound[group] >= SoundGapMs)
        {
            lastSound[group] = now;
            PlayAlert(alert);
        }

        foreach (var window in ChatWindows)
        {
            if (window.IsOnScreen && ReferenceEquals(window.Selected, conversation)) return;
        }

        // Only a closed window is opened. One that is already open is showing
        // something the user chose, and the sidebar badge is enough.
        var home = WindowFor(conversation.Group);
        if (conversation.IsTell && Config.OpenOnIncomingTell && !home.IsOpen && !Services.Condition[ConditionFlag.InCombat])
        {
            home.Show(conversation, focus: false);
            return;
        }

        if (!alert.Toast || reading) return;
        if (lastToast.TryGetValue(conversation.Key, out var shown) && now - shown < ToastGapMs) return;
        lastToast[conversation.Key] = now;
        ShowToast(conversation, message);
    }

    // ------------------------------------------------------------------
    // Per-frame work
    // ------------------------------------------------------------------

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (disposed) return;

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            TrackCharacter();
            Store.Tick();
            Outgoing.Update();

            var tick = Environment.TickCount64;
            if (tick >= nextSlotRefresh)
            {
                nextSlotRefresh = tick + SlotRefreshMs;
                RefreshLinkshells();
            }

            // Each window says what it is showing while it draws. One that has
            // stopped drawing, hidden with the rest of the UI say, is not being
            // read however open it is.
            var tellsOnScreen = false;
            string? tellOnScreen = null;
            foreach (var window in ChatWindows)
            {
                if (!window.IsOnScreen)
                {
                    if (Store.Viewed(window.Viewer) != null) Store.SetViewed(window.Viewer, null);
                    continue;
                }

                if (window.CurrentGroup != ChannelGroup.Tell) continue;
                tellsOnScreen = true;
                if (window.Selected is { IsTell: true } shown) tellOnScreen ??= shown.Key;
            }

            // Friends' statuses come from the client's copy of the friend
            // list, which is only refreshed when someone asks for it: on a
            // schedule while tells are on screen, and straight away when a
            // different tell is opened, so its status is current.
            if (tellsOnScreen && Store.HasCharacter && Config.ShowFriendStatus && Config.RefreshFriendList)
            {
                if (!string.Equals(tellOnScreen, lastTellOnScreen, StringComparison.OrdinalIgnoreCase)) FriendList.RequestSoon(Config.FriendRefreshSeconds);
                else FriendList.RequestIfStale(Config.FriendRefreshSeconds);
            }
            lastTellOnScreen = tellsOnScreen ? tellOnScreen : null;

            HandleWindowKeys();
            UpdateGeneral(tick);
            UpdateGameChat(tick);

            dtr?.Update();

            if (configDirtySince != 0 && tick - configDirtySince >= ConfigDebounceMs) WriteConfig();
        }
        catch (Exception ex)
        {
            // Once a frame is far too often to say the same thing.
            var tick = Environment.TickCount64;
            if (tick - lastErrorLogged > 5000)
            {
                lastErrorLogged = tick;
                Services.Log.Error(ex, "Parley's update failed.");
            }
        }

        NoteSlowWork("its update", started);
    }

    /// <summary>Work on the game's thread that takes this long shows as a hitch, so it is worth a line in the log.</summary>
    private const double SlowWorkMs = 50;

    private long lastSlowWorkLogged;

    /// <summary>
    /// Logs, at most every ten seconds, when something Parley did on the
    /// game's thread took long enough for the game to stutter, with what the
    /// game was doing, so a hitch someone notices can be traced to Parley or
    /// ruled out.
    /// </summary>
    /// <param name="started">A <see cref="System.Diagnostics.Stopwatch"/> timestamp from when it began.</param>
    internal void NoteSlowWork(string what, long started)
    {
        var took = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (took < SlowWorkMs) return;

        var tick = Environment.TickCount64;
        if (tick - lastSlowWorkLogged < 10_000) return;
        lastSlowWorkLogged = tick;

        var context = GameChatWouldHide() ? "while the game's chat would be hidden" : "with the game's chat in view";
        Services.Log.Information($"Parley took {took:0} ms over {what}, {context}.");
    }

    /// <summary>Notices logging in, logging out and switching character, by watching whose content id is loaded.</summary>
    private void TrackCharacter()
    {
        var player = Services.PlayerState;
        var id = Services.ClientState.IsLoggedIn && player.IsLoaded ? player.ContentId : 0UL;
        if (id == contentId) return;

        contentId = id;
        Outgoing.Clear();
        Array.Clear(emptyStreak);
        Array.Clear(slotsSeen);

        FriendList.Forget();
        General.Clear();
        nextGeneralTabsRead = 0;
        generalShownThisLogin = false;

        if (id == 0)
        {
            Store.UnloadCharacter();
            Linkshells.Clear();
            LocalName = string.Empty;
            LocalWorldId = 0;
            return;
        }

        LocalName = player.CharacterName;
        var world = player.HomeWorld.RowId;
        LocalWorldId = world <= ushort.MaxValue ? (ushort)world : (ushort)0;

        var pruneBefore = Config.RetentionDays > 0
            ? DateTimeOffset.UtcNow.AddDays(-Config.RetentionDays).ToUnixTimeMilliseconds()
            : 0;

        ApplyConfig();
        Store.LoadCharacter(id, LocalName, Worlds.Name(LocalWorldId), Config.SaveHistory, pruneBefore);
        nextSlotRefresh = 0;
    }

    private void ApplySlots(ChannelGroup group, IReadOnlyList<string> names)
    {
        var index = (int)group;
        var any = false;
        foreach (var name in names)
        {
            if (name.Length == 0) continue;
            any = true;
            break;
        }

        if (any)
        {
            emptyStreak[index] = 0;
            slotsSeen[index] = true;
        }
        else if (slotsSeen[index] && ++emptyStreak[index] < EmptyListPatience)
        {
            // Had linkshells a moment ago and now reads as having none. Wait
            // to see whether that lasts before acting on it.
            return;
        }

        Store.SetSlots(group, names);
    }

    /// <summary>
    /// The chat windows' own shortcuts, while one of them has focus.
    ///
    /// Dalamud only hands ordinary keys to a plugin's window while a text box
    /// in it is being typed in; the rest of the time they go to the game, and
    /// the window never hears of them. So Ctrl+C on highlighted text, Ctrl+F
    /// and Alt+R are read here, from the game's own view of the keyboard, and
    /// taken out of it before the game reads it this frame, so the game does
    /// not open its currency window or reply to a tell as well. While a box
    /// is being typed in, Dalamud keeps those keys from the game and the
    /// window handles them itself.
    ///
    /// Ctrl+C is only taken with text highlighted. Without, it stays the
    /// game's, as it does with the game's own chat log.
    /// </summary>
    private void HandleWindowKeys()
    {
        MainWindow? focused = null;
        foreach (var window in ChatWindows)
        {
            if (!window.IsOpen || !window.IsFocused) continue;
            focused = window;
            break;
        }

        // The window with focus is the chat in use. It stays so after Enter
        // hands the keyboard back (see UsingChat), until a click lands
        // somewhere other than Parley.
        NoticeClicksElsewhere();
        if (focused != null) chatInUse = focused;
        if (chatInUse is { IsOpen: false }) chatInUse = null;

        var keys = Services.KeyState;
        var ctrl = keys[VirtualKey.CONTROL];
        var alt = keys[VirtualKey.MENU];

        // Enter, as in the game: into the reply box of the Parley window in
        // use, instead of the game's chat. Never while one of the game's own
        // text boxes is being typed in, where Enter is how that is sent.
        // While Parley stands in for the game's chat, Enter anywhere else opens
        // General's box, the way it opens the game's chat box.
        Action? onEnter = null;
        if (!GameTextInputActive())
        {
            if (Config.EnterOpensReply && chatInUse is { IsOnScreen: true, AcceptsEnter: true } window) onEnter = window.StartTyping;
            else if (ReplacingGameChat && MainWindow is { IsOnScreen: true, AcceptsEnter: true }) onEnter = MainWindow.StartTyping;
            else if (ReplacingGameChat) onEnter = () => MainWindow.TypeInGeneral();
        }
        enterKeyHeld = TakeKey(onEnter != null && !ctrl && !alt, VirtualKey.RETURN, enterKeyHeld, () => onEnter!());

        // Alt+Enter, from anywhere: Parley, ready to type, opened first if need be.
        altEnterHeld = TakeKey(Config.AltEnterOpensParley && alt && !ctrl && Store.HasCharacter && !GameTextInputActive(),
            VirtualKey.RETURN, altEnterHeld, OpenForTyping);

        if (focused == null)
        {
            copyKeyHeld = findKeyHeld = cycleKeyHeld = false;
            return;
        }

        copyKeyHeld = TakeKey(ctrl && !alt && focused.HasSelection, VirtualKey.C, copyKeyHeld, focused.RequestCopy);
        findKeyHeld = TakeKey(ctrl && !alt && Store.HasCharacter, VirtualKey.F, findKeyHeld, focused.RequestSearch);

        var step = keys[VirtualKey.SHIFT] ? -1 : 1;
        cycleKeyHeld = TakeKey(Config.CycleWithAltR && alt && !ctrl, VirtualKey.R, cycleKeyHeld, () => focused.RequestCycle(step));
    }

    /// <summary>
    /// Alt+Enter: an open chat window comes to the front with the cursor in
    /// its reply box, keeping its conversation. With none open, Parley opens
    /// on the conversation with the newest message someone sent.
    /// </summary>
    private void OpenForTyping()
    {
        var window = chatInUse is { IsOpen: true } inUse ? inUse : null;
        if (window == null)
        {
            foreach (var open in ChatWindows)
            {
                if (!open.IsOpen) continue;
                window = open;
                break;
            }
        }

        if (window == null)
        {
            if (Store.NewestIncoming() is { } latest)
            {
                ShowConversation(latest);
                window = WindowFor(latest.Group);
            }
            else
            {
                MainWindow.Show();
                window = MainWindow;
            }
        }

        window.StartTyping();
        chatInUse = window;
    }

    /// <summary>A chat window has just handed the keyboard back with Enter: it is still the chat in use, for the next Enter.</summary>
    internal void UsingChat(MainWindow window) => chatInUse = window;

    /// <summary>
    /// A click anywhere but a Parley window means Parley is no longer the chat
    /// in use, and Enter goes back to the game's chat.
    ///
    /// Read from Windows rather than from ImGui: Dalamud only passes a click
    /// on to ImGui when it lands on one of its windows, so a click on the
    /// game itself would never be seen there.
    /// </summary>
    private void NoticeClicksElsewhere()
    {
        var down = NativeInput.MouseButtonDown();
        var pressed = down && !mouseWasDown;
        mouseWasDown = down;
        if (!pressed || chatInUse == null) return;

        foreach (var window in ChatWindows)
        {
            if (window.IsOpen && window.IsHovered) return;
        }
        chatInUse = null;
    }

    private static class NativeInput
    {
        private const int LeftButton = 0x01;
        private const int RightButton = 0x02;

        public static bool MouseButtonDown() => (GetAsyncKeyState(LeftButton) & 0x8000) != 0 || (GetAsyncKeyState(RightButton) & 0x8000) != 0;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);
    }

    /// <summary>Whether one of the game's own text boxes, such as its chat box, is being typed in.</summary>
    private static unsafe bool GameTextInputActive()
    {
        try
        {
            var module = FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkModule.Instance();
            return module != null && module->IsTextInputActive();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Takes one key away from the game if it is down with the right
    /// modifiers, and acts on it once per press. Returns whether it was taken,
    /// which is what <paramref name="held"/> should be next time.
    /// </summary>
    private static bool TakeKey(bool modifiers, VirtualKey key, bool held, Action act)
    {
        var keys = Services.KeyState;
        if (!modifiers || !keys[key]) return false;

        keys[key] = false;
        if (!held) act();
        return true;
    }

    private string? PlayAlert(ChannelAlert alert)
    {
        try
        {
            return alerts.Play(alert);
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Could not play a notification sound.");
            return ex.Message;
        }
    }

    private void OnSendFailed(Conversation conversation, string text, string reason)
    {
        Store.AddNotice(conversation, $"Not sent: {reason}", error: true);

        // Hand the text back rather than lose it, unless something else has
        // been typed in the meantime.
        if (conversation.Draft.Length == 0) Store.SetDraft(conversation, text);
    }

    private void ShowToast(Conversation conversation, ChatMessage message)
    {
        var preview = message.Text.Length > ToastPreviewLength
            ? message.Text[..(char.IsHighSurrogate(message.Text[ToastPreviewLength - 1]) ? ToastPreviewLength - 1 : ToastPreviewLength)] + "…"
            : message.Text;

        var notification = Services.Notifications.AddNotification(new Notification
        {
            Title = conversation.IsTell ? $"Tell from {conversation.Title}" : conversation.Title,
            Content = conversation.IsTell ? preview : $"{message.Sender}: {preview}",
            Type = NotificationType.Info,
            InitialDuration = TimeSpan.FromSeconds(6),
        });
        notification.Click += _ => ShowConversation(conversation);
    }

    // ------------------------------------------------------------------
    // Commands and plumbing
    // ------------------------------------------------------------------

    private void OpenMainUi() => MainWindow.Show();

    private void OnCommand(string command, string arguments)
    {
        var argument = arguments.Trim();
        if (argument.Length == 0)
        {
            ToggleWindow();
            return;
        }

        switch (argument.ToLowerInvariant())
        {
            case "config" or "settings":
                OpenSettings();
                return;

            case "read":
                var cleared = Store.MarkAllRead();
                Services.Chat.Print(cleared == 0 ? "Nothing was unread." : $"Marked {cleared} {(cleared == 1 ? "message" : "messages")} as read.", "Parley");
                return;

            case "friends":
                ReportFriends();
                return;

            case "tabs":
                ReportTabs();
                return;
        }

        if (!Store.HasCharacter)
        {
            Services.Chat.PrintError("Log in first.", "Parley");
            return;
        }

        if (PlayerName.TrySplit(argument, out var name, out var world))
        {
            var worldId = Worlds.Id(world);
            if (worldId == 0 || !PlayerName.IsPlausible(name))
            {
                Services.Chat.PrintError($"\"{argument}\" is not a player. Use First Last@World.", "Parley");
                return;
            }

            OpenTell(name, worldId);
            return;
        }

        if (Store.FindTellByName(argument) is { } found)
        {
            ShowConversation(found);
            return;
        }

        Services.Chat.PrintError($"No conversation matches \"{argument}\". To start one, use {Command} First Last@World.", "Parley");
    }

    /// <summary>
    /// What Parley can see of the friend list, for working out why a friend
    /// shows no status. Asks for the list again as well.
    /// </summary>
    private void ReportFriends()
    {
        var (friends, online) = FriendList.Count();
        if (friends == 0)
        {
            Services.Chat.Print("Parley sees no friends yet: the game has not fetched your friend list. Parley has just asked for it; opening Social → Friend List once also fetches it.", "Parley");
        }
        else
        {
            var matched = new List<string>();
            foreach (var conversation in Store.InGroup(ChannelGroup.Tell))
            {
                var status = FriendList.Status(conversation.Title, conversation.WorldId, Worlds);
                if (status.IsFriend) matched.Add($"{conversation.Title}: {status.DescribeWithPlace(conversation.WorldId).Replace('\n', ' ')}");
            }
            Services.Chat.Print($"Parley sees {friends} friends, {online} of them online. {matched.Count} of your tells are with friends.", "Parley");
            foreach (var line in matched.Take(10)) Services.Chat.Print(line, "Parley");
            if (!Config.ShowFriendStatus) Services.Chat.Print("Showing friends is turned off in Parley's settings (General → Friends).", "Parley");
            if (FriendList.GameWindowOpen()) Services.Chat.Print("The game's Friend List is open, so Parley shows what it shows and asks for nothing until it closes.", "Parley");
        }

        FriendList.Forget();
        FriendList.RequestIfStale(Config.FriendRefreshSeconds);
    }

    /// <summary>The kinds of line /parley tabs checks, by the names the game's log filter settings use for them.</summary>
    private static readonly (string Name, ushort Info)[] TabReportKinds =
    [
        ("Say", ChatLogFilter.Pack(10)), ("Shout", ChatLogFilter.Pack(11)), ("Yell", ChatLogFilter.Pack(30)),
        ("Tell", ChatLogFilter.Pack(13)), ("Party", ChatLogFilter.Pack(14)), ("Alliance", ChatLogFilter.Pack(15)),
        ("Free Company", ChatLogFilter.Pack(24)),
        ("LS1", ChatLogFilter.Pack(16)), ("LS2", ChatLogFilter.Pack(17)), ("LS3", ChatLogFilter.Pack(18)), ("LS4", ChatLogFilter.Pack(19)),
        ("LS5", ChatLogFilter.Pack(20)), ("LS6", ChatLogFilter.Pack(21)), ("LS7", ChatLogFilter.Pack(22)), ("LS8", ChatLogFilter.Pack(23)),
        ("CWLS1", ChatLogFilter.Pack(37)), ("CWLS2", ChatLogFilter.Pack(101)), ("CWLS3", ChatLogFilter.Pack(102)), ("CWLS4", ChatLogFilter.Pack(103)),
        ("CWLS5", ChatLogFilter.Pack(104)), ("CWLS6", ChatLogFilter.Pack(105)), ("CWLS7", ChatLogFilter.Pack(106)), ("CWLS8", ChatLogFilter.Pack(107)),
        ("Novice Network", ChatLogFilter.Pack(27)), ("Emotes", ChatLogFilter.Pack(29)), ("Custom emotes", ChatLogFilter.Pack(28)),
        ("Echo", ChatLogFilter.Pack(56)), ("System messages", ChatLogFilter.Pack(57)), ("Errors", ChatLogFilter.Pack(60)),
        ("NPC dialogue", ChatLogFilter.Pack(61)), ("NPC announcements", ChatLogFilter.Pack(68)), ("Loot", ChatLogFilter.Pack(62)),
        ("Progress", ChatLogFilter.Pack(64)), ("Crafting", ChatLogFilter.Pack(66)), ("Gathering", ChatLogFilter.Pack(67)),
        ("FC announcements", ChatLogFilter.Pack(69)), ("FC logins", ChatLogFilter.Pack(70)), ("Retainer sales", ChatLogFilter.Pack(71)),
        ("Your damage", ChatLogFilter.Pack(41, source: 1)), ("Your healing", ChatLogFilter.Pack(45, source: 1)),
    ];

    /// <summary>
    /// What Parley reads of the game's chat tabs and chat box, to compare with
    /// the game's own log filter settings before General Chat is built on it.
    /// </summary>
    private void ReportTabs()
    {
        var (channel, label, tellTo) = GameChatTabs.CurrentChannel();
        var on = label.Length > 0 ? $"\"{label}\"" : "an unnamed channel";
        Services.Chat.Print($"Your chat box is on {on} (channel {channel}){(tellTo.Length > 0 ? $", with tells going to {tellTo}" : string.Empty)}.", "Parley");

        var tabs = GameChatTabs.Read();
        if (tabs.Count == 0)
        {
            Services.Chat.PrintError("Parley could not find the game's chat tabs.", "Parley");
            return;
        }

        foreach (var tab in tabs)
        {
            var title = $"Tab {tab.Index + 1} \"{tab.Name}\"{(tab.InUse ? string.Empty : " (not in use)")}";
            if (tab.Hidden == null)
            {
                Services.Chat.Print($"{title}: Parley could not read which lines it shows.", "Parley");
                continue;
            }

            var shown = new List<string>();
            var left = new List<string>();
            foreach (var (name, info) in TabReportKinds) (ChatLogFilter.Shows(tab.Hidden, info) ? shown : left).Add(name);

            var summary = left.Count == 0 ? "shows everything Parley checked"
                : shown.Count == 0 ? "shows none of what Parley checked"
                : left.Count <= shown.Count ? "shows all but " + string.Join(", ", Grouped(left))
                : "shows only " + string.Join(", ", Grouped(shown));
            Services.Chat.Print($"{title}: {summary}.", "Parley");
        }

        Services.Chat.Print("Compare these with the game's log filters, in Character Configuration → Log Window Settings.", "Parley");

        // All eight linkshells, or all eight cross-world ones, read as one.
        static IEnumerable<string> Grouped(List<string> names)
        {
            var ls = names.Count(name => name.StartsWith("LS", StringComparison.Ordinal));
            var cwls = names.Count(name => name.StartsWith("CWLS", StringComparison.Ordinal));
            foreach (var name in names)
            {
                if (ls == 8 && name.StartsWith("LS", StringComparison.Ordinal)) { if (name == "LS1") yield return "Linkshells"; continue; }
                if (cwls == 8 && name.StartsWith("CWLS", StringComparison.Ordinal)) { if (name == "CWLS1") yield return "Cross-world linkshells"; continue; }
                yield return name;
            }
        }
    }

    /// <summary>
    /// Saves the settings. Only turning them into text happens here, on the
    /// game's thread, where they belong. Putting that on disk can stall for a
    /// moment, which on the game's thread would be a moment the game stood
    /// still, so it happens on a worker, one write after another.
    /// </summary>
    /// <param name="wait">Wait for the file to be written, as when Parley is closing.</param>
    private void WriteConfig(bool wait = false)
    {
        configDirtySince = 0;
        string json;
        try
        {
            json = ConfigurationFile.Serialize(Config);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not save Parley's settings.");
            return;
        }

        var path = configPath;
        configWrite = configWrite.ContinueWith(_ =>
        {
            try
            {
                ConfigurationFile.Write(path, json);
            }
            catch (Exception ex)
            {
                LogIoError("save its settings", ex);
            }
        }, TaskScheduler.Default);

        if (wait) configWrite.Wait(5000);
    }

    /// <summary>History and settings I/O happens off the framework thread; this is safe to call from there.</summary>
    private static void LogIoError(string what, Exception ex) => Services.Log.Error(ex, $"Parley could not {what}.");
}
