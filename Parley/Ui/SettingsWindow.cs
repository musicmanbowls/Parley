using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Parley.Core;
using Parley.Core.Settings;
using Parley.Core.Theme;

namespace Parley.Ui;

/// <summary>
/// Settings. Drawn in Dalamud's own style whatever theme the chat window is
/// using, so a custom theme gone wrong can always be fixed from here.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private static readonly (ThemeMode Value, string Label)[] Themes =
    [
        (ThemeMode.Dalamud, "Dalamud (default)"),
        (ThemeMode.Game, "Match the game's UI theme"),
        (ThemeMode.Umbra, "Match Umbra's colour profile"),
        (ThemeMode.Custom, "Custom"),
    ];

    private static readonly (MessageStyle Value, string Label)[] MessageStyles =
    [
        (MessageStyle.Bubbles, "Bubbles: theirs on the left, yours on the right"),
        (MessageStyle.Log, "Log: everything on the left under a name"),
    ];

    private static readonly (TimestampStyle Value, string Label)[] TimestampStyles =
    [
        (TimestampStyle.Dividers, "Only where there is a gap in the conversation"),
        (TimestampStyle.EveryMessage, "On every message"),
    ];

    private static readonly (FontChoice Value, string Label)[] Fonts =
    [
        (FontChoice.Dalamud, "Dalamud's font"),
        (FontChoice.GameAxis, "The game's font (AXIS)"),
    ];

    private static readonly (DtrMode Value, string Label)[] DtrModes =
    [
        (DtrMode.Auto, "Only when no Umbra widget is showing it"),
        (DtrMode.Always, "Always"),
        (DtrMode.Off, "Never"),
    ];

    /// <summary>Parley's own sounds, in the order the sound menu lists them.</summary>
    private static readonly AlertSound[] OwnSounds = [AlertSound.None, AlertSound.Chime, AlertSound.Ping, AlertSound.Bell, AlertSound.Pop];

    private readonly Plugin plugin;
    private readonly Configuration config;
    private readonly FileDialogManager fileDialog = new();

    /// <summary>What went wrong the last time each kind of conversation's sound was tried, if anything.</summary>
    private readonly Dictionary<ChannelGroup, string?> alertErrors = [];

    // Sliders that rebuild something expensive edit a copy and commit on release.
    private float pendingFontScale;
    private bool editingFontScale;

    public SettingsWindow(Plugin plugin)
        : base("Parley Settings###ParleySettings")
    {
        this.plugin = plugin;
        config = plugin.Config;

        Size = new Vector2(560, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("##parleysettings"))
        {
            try
            {
                Tab("General", DrawGeneral);
                Tab("Appearance", DrawAppearance);
                Tab("Notifications", DrawNotifications);
                Tab("History", DrawHistory);
                Tab("Umbra", DrawUmbra);
            }
            finally
            {
                ImGui.EndTabBar();
            }
        }

        // The file picker for sound files is a window of its own while open.
        fileDialog.Draw();
    }

    private static void Tab(string label, Action draw)
    {
        if (!ImGui.BeginTabItem(label)) return;

        // Each page scrolls on its own, so the tab bar stays put.
        ImGui.BeginChild(label);
        try
        {
            draw();
        }
        finally
        {
            ImGui.EndChild();
            ImGui.EndTabItem();
        }
    }

    // ------------------------------------------------------------------
    // General
    // ------------------------------------------------------------------

    private void DrawGeneral()
    {
        Heading("Conversations to keep");
        Toggle("Tells", config.CaptureTells, value => config.CaptureTells = value);
        Toggle("Free Company", config.CaptureFreeCompany, value => config.CaptureFreeCompany = value);
        Toggle("Linkshells", config.CaptureLinkshells, value => config.CaptureLinkshells = value);
        Toggle("Cross-world linkshells", config.CaptureCrossWorld, value => config.CaptureCrossWorld = value);

        Heading("Window");
        Toggle("Open on the newest unread conversation", config.JumpToUnreadOnOpen, value => config.JumpToUnreadOnOpen = value);
        Toggle("Put the cursor in the reply box when opening", config.FocusInputOnOpen, value => config.FocusInputOnOpen = value);
        Toggle("Open when a tell arrives", config.OpenOnIncomingTell, value => config.OpenOnIncomingTell = value,
            "Opens the window on that tell without taking the keyboard, so you keep moving.\nNot while in combat, and not if the window is already open on something else.");
        Toggle("Treat whatever is on screen as read", config.ReadWhenVisible, value => config.ReadWhenVisible = value,
            "Off: the open conversation is marked read while the window has focus or the cursor is over it, so a message that arrives while you are busy elsewhere still counts as unread.\nOn: it is marked read for as long as it is visible.");
        Toggle("Escape closes the window", config.CloseWithEscape, value => config.CloseWithEscape = value,
            "Follows Dalamud's own setting for closing windows with Escape; this can only turn it off for Parley.");
        Toggle("Alt+R goes to the next conversation in the open tab", config.CycleWithAltR, value => config.CycleWithAltR = value,
            "Alt+Shift+R goes back. Only while Parley has focus: anywhere else Alt+R does what it always does in the game.");
        Toggle("Enter hands the keyboard back to the game", config.ReleaseKeyboardOnEnter, value => config.ReleaseKeyboardOnEnter = value,
            "Like the game's own chat box: after Enter, whether or not anything was sent, your keys move your character again without clicking elsewhere first.\nOff: the cursor stays in the reply box for the next message.");
        Toggle("Enter puts you in Parley's reply box while you are using Parley", config.EnterOpensReply, value => config.EnterOpensReply = value,
            "Also like the game: while Parley is the chat you are using (you clicked into it, or just sent a message from it), Enter opens its reply box instead of the game's chat.\nClick anywhere outside Parley and Enter goes back to the game's chat. Never while you are typing in one of the game's own text boxes.");
        Toggle("Alt+Enter brings up Parley, ready to type", config.AltEnterOpensParley, value => config.AltEnterOpensParley = value,
            "From anywhere in the game. An open Parley window comes to the front with the cursor in its reply box.\nA closed one opens on the conversation with the newest message someone sent you.");
        Toggle("Add \"Message in Parley\" to the right-click menu on players", config.ContextMenuEntry, value => config.ContextMenuEntry = value);

        Heading("Windows of their own");
        ImGui.TextDisabled("Any kind of conversation can have a window to itself instead of a tab. Right-click a tab to do the same.");
        foreach (var group in ChannelGroups.All)
        {
            var popped = config.IsPoppedOut(group);
            if (!ImGui.Checkbox($"{group.Label()}##popout", ref popped)) continue;
            if (popped) plugin.PopOut(group);
            else plugin.DockBack(group);
        }

        Heading("Friends");
        Toggle("Show who is on your friend list in the list of tells", config.ShowFriendStatus, value => config.ShowFriendStatus = value,
            "A friend's avatar gets a dot: green online, amber away, red in a duty, a do-not-disturb sign when they have set themselves busy, and a grey ring when offline.\nOffline friends are faded, and the reply box warns when a tell is unlikely to be seen.");
        if (config.ShowFriendStatus)
        {
            Toggle("Keep their status up to date", config.RefreshFriendList, value => config.RefreshFriendList = value,
                "While tells are on screen, and whenever you open a friend's tell, Parley asks the server for your friend list, as opening the game's friend list does.\nOff: statuses are only as fresh as the last time the game fetched the list itself.");
            if (config.RefreshFriendList)
            {
                ImGui.Indent(16f * ImGuiHelpers.GlobalScale);
                var seconds = config.FriendRefreshSeconds;
                ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
                if (ImGui.SliderInt("Every (seconds)##friendrefresh", ref seconds, 5, 60))
                {
                    config.FriendRefreshSeconds = Math.Clamp(seconds, 5, 600);
                    Changed();
                }
                Help("How soon a friend going into a duty, going busy or logging off shows. Each fetch is one small request to the server; 5 seconds is as often as Parley will ask.");
                ImGui.Unindent(16f * ImGuiHelpers.GlobalScale);
            }
        }

        Heading("Sending");
        Toggle("Split messages that are too long for one line", config.SplitLongMessages, value => config.SplitLongMessages = value,
            "The game limits a chat line to 500 bytes, including the command and the name it is addressed to.");
        if (config.SplitLongMessages)
        {
            var delay = config.SplitDelayMs;
            ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderInt("Pause between the parts (ms)", ref delay, 300, 5000))
            {
                config.SplitDelayMs = Math.Clamp(delay, 300, 5000);
                Changed();
            }
            Help("The game refuses chat that is sent too quickly. A longer pause keeps the later parts from being refused.");
        }

        Heading("Keep out of the game's chat log");
        ImGui.TextDisabled("Messages Parley keeps are normally shown in the game's chat log as well.");
        Toggle("Tells##hide", config.HideTellsFromGameChat, value => config.HideTellsFromGameChat = value);
        Toggle("Free Company##hide", config.HideFreeCompanyFromGameChat, value => config.HideFreeCompanyFromGameChat = value);
        Toggle("Linkshells##hide", config.HideLinkshellsFromGameChat, value => config.HideLinkshellsFromGameChat = value);
        Toggle("Cross-world linkshells##hide", config.HideCrossWorldFromGameChat, value => config.HideCrossWorldFromGameChat = value);
        ImGui.TextDisabled("A hidden message never reaches the game's log, so other chat plugins will not see it either.");

        Heading("Show the window");
        Toggle("During cutscenes", config.ShowInCutscenes, value => config.ShowInCutscenes = value);
        Toggle("In group pose", config.ShowInGpose, value => config.ShowInGpose = value);
        Toggle("While the game's UI is hidden", config.ShowWhenUiHidden, value => config.ShowWhenUiHidden = value);

        Heading("Commands");
        ImGui.TextUnformatted("/parley");
        ImGui.SameLine(200f * ImGuiHelpers.GlobalScale);
        ImGui.TextDisabled("open or close the chat window");
        ImGui.TextUnformatted("/parley First Last@World");
        ImGui.SameLine(200f * ImGuiHelpers.GlobalScale);
        ImGui.TextDisabled("open a tell with that player");
        ImGui.TextUnformatted("/parley <part of a name>");
        ImGui.SameLine(200f * ImGuiHelpers.GlobalScale);
        ImGui.TextDisabled("open an existing conversation");
        ImGui.TextUnformatted("/parley read");
        ImGui.SameLine(200f * ImGuiHelpers.GlobalScale);
        ImGui.TextDisabled("mark everything as read");
    }

    // ------------------------------------------------------------------
    // Appearance
    // ------------------------------------------------------------------

    private void DrawAppearance()
    {
        Heading("Theme");
        if (Choice("Colours", Themes, config.Theme, out var theme))
        {
            // Starting a custom theme from a blank would be starting from the
            // dark preset whatever the window looked like. Start from what is
            // on screen instead.
            if (theme == ThemeMode.Custom && config.CustomColours.Count == 0)
            {
                plugin.Theme.Resolve();
                config.CustomColours = plugin.Theme.Palette.ToHex();
                plugin.Theme.InvalidateCustom();
            }

            config.Theme = theme;
            Changed();
        }

        switch (config.Theme)
        {
            case ThemeMode.Game:
                DrawGameThemeChoice();
                break;

            case ThemeMode.Umbra when plugin.Theme.UmbraAvailable:
                ImGui.TextDisabled("Following the colour profile Umbra is using.");
                break;

            case ThemeMode.Umbra:
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.75f, 0.35f, 1f));
                ImGui.TextWrapped("Umbra has not sent its colours, so Dalamud's style is in use for now. This needs the Parley companion installed in Umbra; see the Umbra tab.");
                ImGui.PopStyleColor();
                break;

            case ThemeMode.Custom:
                DrawCustomTheme();
                break;
        }

        Toggle("Use the game's chat colours for tells and linkshells", config.UseGameChatColours, value => config.UseGameChatColours = value,
            "Tints each tab, and each linkshell, with the colour set for that channel under\nCharacter Configuration → Log Window Settings.");

        if (config.Theme != ThemeMode.Dalamud)
        {
            var rounding = config.CornerRounding;
            ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("Corner rounding", ref rounding, 0f, 16f, "%.0f"))
            {
                config.CornerRounding = rounding;
                Changed();
            }
        }

        var opacity = config.WindowOpacity * 100f;
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("Window opacity", ref opacity, 30f, 100f, "%.0f%%"))
        {
            config.WindowOpacity = Math.Clamp(opacity / 100f, 0.3f, 1f);
            Changed();
        }

        Heading("Messages");
        if (Choice("Layout", MessageStyles, config.MessageStyle, out var style))
        {
            config.MessageStyle = style;
            Changed();
        }

        if (Choice("Show the time", TimestampStyles, config.Timestamps, out var timestamps))
        {
            config.Timestamps = timestamps;
            Changed();
        }

        Toggle("24-hour clock", config.Use24Hour, value => config.Use24Hour = value);
        Toggle("Hovering an item link shows the game's item tooltip", config.NativeItemTooltips, value => config.NativeItemTooltips = value,
            "The same tooltip the game's chat log shows. Off: a simpler one drawn by Parley, with the item's name, kind and description.");

        Heading("Names");
        Toggle("Give each person their own name colour", config.ColourNames, value => config.ColourNames = value,
            "Worked out from the name, so it is always the same for the same person. Off: names are in the colour of the channel.");
        DrawNameColours();

        Heading("Text");
        if (Choice("Font", Fonts, config.Font, out var font))
        {
            config.Font = font;
            Changed();
        }

        if (!editingFontScale) pendingFontScale = config.FontScale * 100f;
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        ImGui.SliderFloat("Size", ref pendingFontScale, 70f, 200f, "%.0f%%");
        editingFontScale = ImGui.IsItemActive();
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            // Applied on release: every change rebuilds the font.
            config.FontScale = Math.Clamp(pendingFontScale / 100f, 0.7f, 2f);
            Changed();
        }

        Heading("Conversation list");
        Toggle("Show the last message under each name", config.SidebarPreviews, value => config.SidebarPreviews = value);
        Toggle("Shrink to icons only", config.SidebarCollapsed, value => config.SidebarCollapsed = value,
            "The same as the arrow button at the top of a conversation, or double-clicking the divider.");
    }

    /// <summary>Which of the game's themes to look like: whichever the game is set to, or one picked for Parley alone.</summary>
    private void DrawGameThemeChoice()
    {
        var following = !GameThemes.IsKnown(config.GameThemeOverride);
        var sameAsGame = $"Same as the game (currently {GameThemes.Name(plugin.Theme.GameTheme)})";

        ImGui.SetNextItemWidth(340f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("Game theme", following ? sameAsGame : GameThemes.Name(config.GameThemeOverride)))
        {
            try
            {
                if (ImGui.Selectable(sameAsGame, following) && !following)
                {
                    config.GameThemeOverride = -1;
                    Changed();
                }

                ImGui.Separator();
                for (var theme = 0; theme < GameThemes.Count; theme++)
                {
                    var current = config.GameThemeOverride == theme;
                    if (!ImGui.Selectable(GameThemes.Name(theme), current) || current) continue;
                    config.GameThemeOverride = theme;
                    Changed();
                }
            }
            finally
            {
                ImGui.EndCombo();
            }
        }

        ImGui.TextDisabled(following
            ? "Changes along with the theme picked in the game's System Configuration."
            : "Parley keeps this look whatever the game itself is set to.");
    }

    /// <summary>The colours picked for particular people, each with a way to change it or go back to the automatic one.</summary>
    private void DrawNameColours()
    {
        if (config.NameColours.Count == 0)
        {
            ImGui.TextDisabled("To pick a colour for someone, right-click one of their messages, or their tell in the list.");
            return;
        }

        ImGui.TextDisabled("Picked for particular people:");

        // A copy: removing an entry changes the collection being listed.
        var keys = config.NameColours.Keys.ToArray();
        Array.Sort(keys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (!ColourMath.TryParseHex(config.NameColours[key], out var colour)) continue;

            var rgb = new Vector3(colour.X, colour.Y, colour.Z);
            if (ImGui.ColorEdit3($"{key}##name", ref rgb, ImGuiColorEditFlags.NoInputs))
            {
                config.SetNameColour(key, ColourMath.ToHex(new Vector4(rgb, 1f)));
                plugin.SaveConfig();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton($"Automatic##{key}"))
            {
                config.SetNameColour(key, null);
                plugin.SaveConfig();
            }
        }
    }

    private void DrawCustomTheme()
    {
        ImGui.TextDisabled("Every colour the chat window uses. Changes apply as you make them.");

        if (ImGui.Button("Start from the game's theme"))
            ReplaceCustom(GameThemes.For(plugin.Theme.EffectiveGameTheme));
        ImGui.SameLine();
        ImGui.BeginDisabled(!plugin.Theme.UmbraAvailable);
        if (ImGui.Button("Start from Umbra") && plugin.Theme.CloneUmbra() is { } umbra) ReplaceCustom(umbra);
        ImGui.EndDisabled();

        var working = GameThemes.For(GameThemes.Dark);
        working.ApplyHex(config.CustomColours);

        if (ImGui.BeginTable("##palette", 2, ImGuiTableFlags.SizingStretchProp))
        {
            try
            {
                foreach (var slot in Palette.Slots)
                {
                    var isGroup = slot is PaletteSlot.Tell or PaletteSlot.Linkshell or PaletteSlot.CrossWorld;
                    ImGui.TableNextColumn();
                    ImGui.BeginDisabled(isGroup && config.UseGameChatColours);

                    var colour = working[slot];
                    var edited = ImGui.ColorEdit4($"{Palette.Label(slot)}##{slot}", ref colour,
                        ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf);
                    ImGui.EndDisabled();

                    if (!edited) continue;
                    config.CustomColours[slot.ToString()] = ColourMath.ToHex(colour);
                    plugin.Theme.InvalidateCustom();
                    Changed();
                }
            }
            finally
            {
                ImGui.EndTable();
            }
        }

        if (config.UseGameChatColours)
            ImGui.TextDisabled("The three channel colours are coming from the game. Turn that off below to set your own.");
    }

    private void ReplaceCustom(Palette palette)
    {
        config.CustomColours = palette.ToHex();
        plugin.Theme.InvalidateCustom();
        Changed();
    }

    // ------------------------------------------------------------------
    // Notifications
    // ------------------------------------------------------------------

    private void DrawNotifications()
    {
        Heading("When a message arrives");
        ImGui.TextWrapped("Each kind of conversation has its own sound and notification. Neither happens for a conversation you are reading, or one you have muted, and a busy channel sounds at most once every couple of seconds.");

        foreach (var group in ChannelGroups.All) DrawAlert(group);

        Heading("Server info bar");
        ImGui.TextWrapped("An unread counter beside the clock at the top of the screen. Left-click opens or closes the chat window; right-click marks everything as read.");
        if (Choice("Show it", DtrModes, config.DtrMode, out var mode))
        {
            config.DtrMode = mode;
            Changed();
        }

        if (config.DtrMode != DtrMode.Off)
        {
            ImGui.TextUnformatted("Count");
            ImGui.SameLine();
            Toggle("Tells##dtr", config.DtrCountTells, value => config.DtrCountTells = value);
            ImGui.SameLine();
            Toggle("FC##dtr", config.DtrCountFreeCompany, value => config.DtrCountFreeCompany = value);
            ImGui.SameLine();
            Toggle("Linkshells##dtr", config.DtrCountLinkshells, value => config.DtrCountLinkshells = value);
            ImGui.SameLine();
            Toggle("Cross-world##dtr", config.DtrCountCrossWorld, value => config.DtrCountCrossWorld = value);
        }
    }

    /// <summary>One kind of conversation's sound, volume and notification.</summary>
    private void DrawAlert(ChannelGroup group)
    {
        var alert = config.Alert(group);
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.PushID((int)group);
        try
        {
            ImGui.Dummy(new Vector2(1f, 4f * scale));
            ImGui.TextUnformatted(group.Label());
            ImGui.Indent(16f * scale);

            ImGui.SetNextItemWidth(220f * scale);
            if (ImGui.BeginCombo("Sound", SoundName(alert)))
            {
                try
                {
                    foreach (var sound in OwnSounds)
                    {
                        if (ImGui.Selectable(Sounds.Label(sound), alert.Sound == sound)) PickSound(group, alert, sound);
                    }

                    ImGui.Separator();
                    for (var i = 1; i <= 16; i++)
                    {
                        var current = alert.Sound == AlertSound.Game && alert.GameSound == i;
                        if (!ImGui.Selectable($"Game sound effect {i}", current)) continue;
                        alert.GameSound = i;
                        PickSound(group, alert, AlertSound.Game);
                    }

                    ImGui.Separator();
                    if (ImGui.Selectable("A sound file (WAV)…", alert.Sound == AlertSound.File))
                    {
                        alert.Sound = AlertSound.File;
                        Changed();
                        if (alert.File.Length == 0) ChooseFile(group, alert);
                    }
                }
                finally
                {
                    ImGui.EndCombo();
                }
            }

            if (alert.Sound != AlertSound.None)
            {
                ImGui.SameLine();
                if (ImGui.Button("Test")) alertErrors[group] = plugin.TestAlert(group);
            }

            if (alert.Sound == AlertSound.File)
            {
                ImGui.TextDisabled(alert.File.Length > 0 ? alert.File : "No file chosen yet.");
                ImGui.SameLine();
                if (ImGui.SmallButton("Choose…")) ChooseFile(group, alert);
            }

            if (alert.HasVolume)
            {
                var volume = alert.Volume;
                ImGui.SetNextItemWidth(220f * scale);
                if (ImGui.SliderInt("Volume", ref volume, 0, 100, "%d%%"))
                {
                    alert.Volume = volume;
                    alertErrors.Remove(group);
                    Changed();
                }

                // Hear the new level once the slider is let go, rather than on every step of the drag.
                if (ImGui.IsItemDeactivatedAfterEdit()) alertErrors[group] = plugin.TestAlert(group);
            }
            else if (alert.Sound == AlertSound.Game)
            {
                ImGui.TextDisabled("Plays at the volume of the game's system sounds.");
            }

            var toast = alert.Toast;
            if (ImGui.Checkbox("Show a notification", ref toast))
            {
                alert.Toast = toast;
                Changed();
            }
            Help(group == ChannelGroup.Tell
                ? "A Dalamud notification with the sender and the start of the message. Clicking it opens the conversation."
                : "A Dalamud notification with who spoke and the start of what they said. Clicking it opens the conversation.");

            if (alertErrors.TryGetValue(group, out var error) && error != null)
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.45f, 1f), error);

            ImGui.Unindent(16f * scale);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static string SoundName(ChannelAlert alert) => alert.Sound switch
    {
        AlertSound.Game => $"Game sound effect {alert.GameSound}",
        AlertSound.File => "A sound file (WAV)",
        _ => Sounds.Label(alert.Sound),
    };

    private void PickSound(ChannelGroup group, ChannelAlert alert, AlertSound sound)
    {
        alert.Sound = sound;
        Changed();
        alertErrors[group] = plugin.TestAlert(group);
    }

    private void ChooseFile(ChannelGroup group, ChannelAlert alert)
    {
        fileDialog.OpenFileDialog("Choose a sound", "Sound files{.wav}", (chosen, path) =>
        {
            if (!chosen || string.IsNullOrEmpty(path)) return;
            alert.File = path;
            alert.Sound = AlertSound.File;
            Changed();
            alertErrors[group] = plugin.TestAlert(group);
        });
    }

    // ------------------------------------------------------------------
    // History
    // ------------------------------------------------------------------

    private void DrawHistory()
    {
        Heading("Saved messages");

        var save = config.SaveHistory;
        if (ImGui.Checkbox("Save conversations to disk", ref save))
        {
            config.SaveHistory = save;
            Changed();
            // Fixed for the length of a session inside the store, so the
            // character is loaded again under the new setting.
            plugin.ReloadCharacter();
        }
        Help("Off: conversations last until you log out, and nothing is written.\nTurning this off does not delete what is already saved.");

        ImGui.TextWrapped("Each character has its own folder, with one plain text file per conversation.");
        ImGui.TextDisabled(plugin.HistoryDirectory);
        if (ImGui.Button("Open the folder")) OpenFolder(plugin.HistoryDirectory);

        Heading("Keeping it tidy");
        var days = config.RetentionDays;
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("Delete messages older than (days)", ref days))
        {
            config.RetentionDays = Math.Clamp(days, 0, 3650);
            Changed();
        }
        Help("0 keeps everything. Old messages are removed the next time you log in.");

        Heading("Memory");
        var page = config.PageSize;
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Messages loaded at a time", ref page, 20, 500))
        {
            config.PageSize = Math.Clamp(page, 20, 500);
            Changed();
        }

        var loaded = config.MaxLoadedMessages;
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Most kept in memory per conversation", ref loaded, 100, 5000))
        {
            config.MaxLoadedMessages = Math.Clamp(loaded, 100, 5000);
            Changed();
        }
        Help("Older messages stay on disk and come back when you scroll up past the top, or with \"Load earlier messages\".\nA conversation is only allowed past this while you are reading back through it.");
    }

    // ------------------------------------------------------------------
    // Umbra
    // ------------------------------------------------------------------

    private void DrawUmbra()
    {
        Heading("Toolbar widget");
        ImGui.TextWrapped("Parley has a companion for Umbra that adds a toolbar widget: it shows how many messages are unread, opens this window on a left-click and marks everything read on a right-click.");

        var connected = plugin.Ipc.RecentlyPolled;
        ImGui.TextUnformatted("Widget:");
        ImGui.SameLine();
        if (connected) ImGui.TextColored(new Vector4(0.45f, 0.9f, 0.5f, 1f), "on a toolbar and asking for status");
        else ImGui.TextDisabled("not seen in the last few seconds");

        ImGui.TextUnformatted("Colours:");
        ImGui.SameLine();
        if (plugin.Theme.UmbraAvailable) ImGui.TextColored(new Vector4(0.45f, 0.9f, 0.5f, 1f), "received from Umbra");
        else ImGui.TextDisabled("not received");

        Heading("Installing it");
        ImGui.TextWrapped("1. In Umbra, open Settings → Plugins and enable custom plugins if asked.");
        ImGui.TextWrapped("2. Add the companion: from its repository once it has been published, or with \"Install from file\" and the Umbra.Parley.dll that the build produces.");
        ImGui.TextWrapped("3. Restart Umbra when it asks, then add the \"Parley\" widget to a toolbar.");
        ImGui.TextWrapped("The companion also passes Umbra's colour profile to Parley, which is what the \"Match Umbra's colour profile\" theme uses. It does that whether or not the widget is on a toolbar.");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void Changed()
    {
        config.Clamp();
        plugin.ApplyConfig();
        plugin.SaveConfig();
    }

    private static void Heading(string text)
    {
        ImGui.Dummy(new Vector2(1f, 4f * ImGuiHelpers.GlobalScale));
        ImGui.TextUnformatted(text);
        ImGui.Separator();
    }

    private void Toggle(string label, bool value, Action<bool> set, string? help = null)
    {
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            Changed();
        }
        if (help != null) Help(help);
    }

    /// <summary>A "(?)" after the previous item that explains it on hover.</summary>
    private static void Help(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (!ImGui.IsItemHovered()) return;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(360f * ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static bool Choice<T>(string label, (T Value, string Label)[] options, T current, out T chosen) where T : struct, Enum
    {
        chosen = current;
        var preview = string.Empty;
        foreach (var option in options)
        {
            if (EqualityComparer<T>.Default.Equals(option.Value, current)) preview = option.Label;
        }

        var changed = false;
        ImGui.SetNextItemWidth(340f * ImGuiHelpers.GlobalScale);
        if (!ImGui.BeginCombo(label, preview)) return false;

        foreach (var option in options)
        {
            var isCurrent = EqualityComparer<T>.Default.Equals(option.Value, current);
            if (ImGui.Selectable(option.Label, isCurrent) && !isCurrent)
            {
                chosen = option.Value;
                changed = true;
            }
        }

        ImGui.EndCombo();
        return changed;
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Could not open the history folder.");
        }
    }
}
