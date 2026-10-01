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

            ApplyConfig();

            Services.Commands.AddHandler(Command, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open or close the Parley chat window.\n"
                              + $"{Command} First Last@World → open a tell with that player\n"
                              + $"{Command} <part of a name> → open an existing conversation\n"
                              + $"{Command} read → mark everything as read\n"
                              + $"{Command} friends → check what Parley can see of your friend list\n"
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
        var builder = Services.PluginInterface.UiBuilder;
        builder.Draw -= windows.Draw;
        builder.OpenMainUi -= OpenMainUi;
        builder.OpenConfigUi -= OpenSettings;
        Services.Commands.RemoveHandler(Command);

        capture?.Dispose();
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

        if (configDirtySince != 0) WriteConfig();
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

    internal SendOutcome Send(Conversation conversation, string text) =>
        Store.HasCharacter ? Outgoing.Send(conversation, text) : SendOutcome.CannotSend;

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

        var builder = Services.PluginInterface.UiBuilder;
        builder.DisableCutsceneUiHide = Config.ShowInCutscenes;
        builder.DisableGposeUiHide = Config.ShowInGpose;
        builder.DisableUserUiHide = Config.ShowWhenUiHidden;
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
        var enterTarget = Config.EnterOpensReply && chatInUse is { IsOnScreen: true, AcceptsEnter: true } && !GameTextInputActive() ? chatInUse : null;
        enterKeyHeld = TakeKey(enterTarget != null && !ctrl && !alt, VirtualKey.RETURN, enterKeyHeld, () => enterTarget!.StartTyping());

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

    private void WriteConfig()
    {
        configDirtySince = 0;
        try
        {
            ConfigurationFile.Save(configPath, Config);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Could not save Parley's settings.");
        }
    }

    /// <summary>History and settings I/O happens off the framework thread; this is safe to call from there.</summary>
    private static void LogIoError(string what, Exception ex) => Services.Log.Error(ex, $"Parley could not {what}.");
}
