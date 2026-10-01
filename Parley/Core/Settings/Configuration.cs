using System.Text.Json.Serialization;
using Parley.Core.Theme;

namespace Parley.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
public enum ThemeMode
{
    /// <summary>Draw with whatever style Dalamud is using. Nothing is overridden.</summary>
    Dalamud,

    /// <summary>Follow the UI theme picked in the game's own System Configuration.</summary>
    Game,

    /// <summary>Follow the colour profile active in Umbra. Needs the companion installed.</summary>
    Umbra,

    Custom,
}

[JsonConverter(typeof(JsonStringEnumConverter<MessageStyle>))]
public enum MessageStyle
{
    /// <summary>Messenger layout: their messages on the left, yours on the right.</summary>
    Bubbles,

    /// <summary>Flat layout: everything left-aligned under a name, like a chat log.</summary>
    Log,
}

[JsonConverter(typeof(JsonStringEnumConverter<TimestampStyle>))]
public enum TimestampStyle
{
    /// <summary>A centred time only where a gap in the conversation makes it useful.</summary>
    Dividers,
    EveryMessage,
}

[JsonConverter(typeof(JsonStringEnumConverter<FontChoice>))]
public enum FontChoice
{
    Dalamud,

    /// <summary>The game's own AXIS typeface.</summary>
    GameAxis,
}

[JsonConverter(typeof(JsonStringEnumConverter<DtrMode>))]
public enum DtrMode
{
    Off,

    /// <summary>Show the server info bar entry only while no Umbra widget is asking for status.</summary>
    Auto,
    Always,
}

public sealed class Configuration
{
    public int Version { get; set; } = 1;

    // ---- Behaviour ----
    public bool CaptureTells { get; set; } = true;
    public bool CaptureLinkshells { get; set; } = true;
    public bool CaptureCrossWorld { get; set; } = true;
    public bool CaptureFreeCompany { get; set; } = true;
    public bool OpenOnIncomingTell { get; set; }

    /// <summary>Alt+R moves to the next conversation in the open tab, and Alt+Shift+R to the previous, while the window has focus.</summary>
    public bool CycleWithAltR { get; set; } = true;
    public bool FocusInputOnOpen { get; set; } = true;
    public bool JumpToUnreadOnOpen { get; set; } = true;

    /// <summary>
    /// When set, the conversation on screen counts as read however little
    /// attention the window is getting. Otherwise it only does while the
    /// window has focus or the cursor is over it, so a message that arrives
    /// while you are busy elsewhere still shows up as unread.
    /// </summary>
    public bool ReadWhenVisible { get; set; }
    public bool CloseWithEscape { get; set; } = true;

    /// <summary>
    /// Pressing Enter in the reply box, whether or not anything was sent, hands
    /// the keyboard back to the game, the way the game's own chat box does.
    /// Off: the cursor stays in the reply box for the next message.
    /// </summary>
    public bool ReleaseKeyboardOnEnter { get; set; } = true;

    /// <summary>
    /// The other half of that, also as in the game: while Parley is the chat
    /// being used (it was clicked into, or a message was just sent from it),
    /// Enter puts the cursor in its reply box instead of opening the game's
    /// chat. A click anywhere outside Parley gives Enter back to the game.
    /// </summary>
    public bool EnterOpensReply { get; set; } = true;

    /// <summary>
    /// Alt+Enter, from anywhere in the game: brings Parley up ready to type.
    /// An open window keeps its conversation; a closed one opens on the
    /// conversation with the newest message someone sent.
    /// </summary>
    public bool AltEnterOpensParley { get; set; } = true;

    /// <summary>Hovering an item link shows the game's own item tooltip. Off: a simpler one drawn by Parley.</summary>
    public bool NativeItemTooltips { get; set; } = true;

    /// <summary>Mark people on the friend list in the list of tells, with whether they are online, busy or in a duty.</summary>
    public bool ShowFriendStatus { get; set; } = true;

    /// <summary>
    /// Ask the server for the friend list now and then while tells are on
    /// screen, the way opening the game's friend list does, so the statuses
    /// stay current. Off: they are only as fresh as the last time the game
    /// fetched the list itself.
    /// </summary>
    public bool RefreshFriendList { get; set; } = true;

    /// <summary>How often, in seconds, the friend list is fetched while tells are on screen. 5 at the least.</summary>
    public int FriendRefreshSeconds { get; set; } = 5;

    /// <summary>Kinds of conversation shown in a window of their own rather than as a tab of the main one.</summary>
    public List<ChannelGroup> PoppedOut { get; set; } = [];

    /// <summary>Symbols picked recently, most recent first, for the top of the symbol picker.</summary>
    public List<string> RecentSymbols { get; set; } = [];
    public bool SplitLongMessages { get; set; } = true;
    public int SplitDelayMs { get; set; } = 1100;
    public bool HideTellsFromGameChat { get; set; }
    public bool HideLinkshellsFromGameChat { get; set; }
    public bool HideCrossWorldFromGameChat { get; set; }
    public bool HideFreeCompanyFromGameChat { get; set; }
    public bool ShowInCutscenes { get; set; }
    public bool ShowInGpose { get; set; }
    public bool ShowWhenUiHidden { get; set; }
    public bool ContextMenuEntry { get; set; } = true;

    // ---- Notifications ----

    /// <summary>Sound and notification for each kind of conversation. Filled in by <see cref="Clamp"/>.</summary>
    public Dictionary<ChannelGroup, ChannelAlert> Alerts { get; set; } = [];

    /// <summary>Superseded by <see cref="Alerts"/>. Read from older files, moved across, and not written again.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TellSound { get; set; }

    /// <summary>Superseded by <see cref="Alerts"/>, as <see cref="TellSound"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ToastOnTell { get; set; }

    public DtrMode DtrMode { get; set; } = DtrMode.Auto;
    public bool DtrCountTells { get; set; } = true;
    public bool DtrCountLinkshells { get; set; } = true;
    public bool DtrCountCrossWorld { get; set; } = true;
    public bool DtrCountFreeCompany { get; set; } = true;

    // ---- History ----
    public bool SaveHistory { get; set; } = true;

    /// <summary>Messages older than this many days are removed at login. 0 keeps everything.</summary>
    public int RetentionDays { get; set; }
    public int PageSize { get; set; } = 80;
    public int MaxLoadedMessages { get; set; } = 400;

    // ---- Appearance ----
    public ThemeMode Theme { get; set; } = ThemeMode.Dalamud;

    /// <summary>With <see cref="ThemeMode.Game"/>: which of the game's themes to look like, 0 to 7, or -1 for whichever the game is set to.</summary>
    public int GameThemeOverride { get; set; } = -1;

    /// <summary>Custom theme colours as "#RRGGBBAA", keyed by palette slot name.</summary>
    public Dictionary<string, string> CustomColours { get; set; } = [];

    /// <summary>Tint each tab group with the colour the game's chat log uses for that channel.</summary>
    public bool UseGameChatColours { get; set; } = true;
    public MessageStyle MessageStyle { get; set; } = MessageStyle.Bubbles;
    public TimestampStyle Timestamps { get; set; } = TimestampStyle.Dividers;
    public bool Use24Hour { get; set; } = true;

    /// <summary>Give each person a colour of their own, worked out from their name. Off: names are in the channel's colour.</summary>
    public bool ColourNames { get; set; } = true;

    /// <summary>Colours picked for particular people, as "#RRGGBBAA" keyed by "First Last@World". They win over <see cref="ColourNames"/>.</summary>
    public Dictionary<string, string> NameColours { get; set; } = [];

    /// <summary>Goes up whenever <see cref="NameColours"/> changes, so anything that looked a colour up knows to look again.</summary>
    [JsonIgnore]
    public int NameColoursVersion { get; private set; }
    public FontChoice Font { get; set; } = FontChoice.Dalamud;
    public float FontScale { get; set; } = 1f;
    public float WindowOpacity { get; set; } = 1f;
    public float CornerRounding { get; set; } = 6f;
    public float SidebarWidth { get; set; } = 200f;
    public bool SidebarCollapsed { get; set; }
    public bool SidebarPreviews { get; set; } = true;

    // ---- State ----
    public ChannelGroup LastGroup { get; set; } = ChannelGroup.Tell;

    /// <summary>The alert settings for one kind of conversation, created with the defaults if there are none yet.</summary>
    public ChannelAlert Alert(ChannelGroup group)
    {
        Alerts ??= [];
        if (!Alerts.TryGetValue(group, out var alert) || alert == null)
        {
            alert = new ChannelAlert();
            Alerts[group] = alert;
        }
        return alert;
    }

    /// <summary>The key a person's colour is filed under.</summary>
    public static string NameKey(string name, string world) => world.Length > 0 ? $"{name}@{world}" : name;

    /// <summary>Sets a person's name colour, or with null goes back to the automatic one.</summary>
    public void SetNameColour(string key, string? hex)
    {
        NameColours ??= [];
        if (hex == null) NameColours.Remove(key);
        else NameColours[key] = hex;
        NameColoursVersion++;
    }

    /// <summary>Pulls anything a hand-edited or older file left out of range back into it.</summary>
    /// <summary>Whether this kind of conversation has a window of its own.</summary>
    public bool IsPoppedOut(ChannelGroup group) => PoppedOut != null && PoppedOut.Contains(group);

    public void SetPoppedOut(ChannelGroup group, bool poppedOut)
    {
        PoppedOut ??= [];
        PoppedOut.Remove(group);
        if (poppedOut) PoppedOut.Add(group);
    }

    /// <summary>Puts a symbol at the front of the recent list, keeping the list short.</summary>
    public void UsedSymbol(string symbol)
    {
        RecentSymbols ??= [];
        RecentSymbols.Remove(symbol);
        RecentSymbols.Insert(0, symbol);
        if (RecentSymbols.Count > MaxRecentSymbols) RecentSymbols.RemoveRange(MaxRecentSymbols, RecentSymbols.Count - MaxRecentSymbols);
    }

    public const int MaxRecentSymbols = 12;

    public void Clamp()
    {
        NameColours ??= [];
        PoppedOut = (PoppedOut ?? []).Where(Enum.IsDefined).Distinct().ToList();
        RecentSymbols = (RecentSymbols ?? []).Where(symbol => !string.IsNullOrEmpty(symbol) && symbol.Length <= 8).Distinct().Take(MaxRecentSymbols).ToList();
        foreach (var group in ChannelGroups.All) Alert(group).Clamp();

        // Settings from before each kind of conversation had its own alerts.
        if (TellSound > 0 || ToastOnTell)
        {
            var tell = Alert(ChannelGroup.Tell);
            if (TellSound > 0 && tell.Sound == AlertSound.None)
            {
                tell.Sound = AlertSound.Game;
                tell.GameSound = Math.Clamp(TellSound, 1, 16);
            }
            if (ToastOnTell) tell.Toast = true;
            TellSound = 0;
            ToastOnTell = false;
        }

        SplitDelayMs = Math.Clamp(SplitDelayMs, 300, 5000);
        FriendRefreshSeconds = Math.Clamp(FriendRefreshSeconds, 5, 600);
        RetentionDays = Math.Clamp(RetentionDays, 0, 3650);
        PageSize = Math.Clamp(PageSize, 20, 500);
        MaxLoadedMessages = Math.Clamp(MaxLoadedMessages, 100, 5000);
        FontScale = float.IsFinite(FontScale) ? Math.Clamp(FontScale, 0.7f, 2f) : 1f;
        WindowOpacity = float.IsFinite(WindowOpacity) ? Math.Clamp(WindowOpacity, 0.3f, 1f) : 1f;
        CornerRounding = float.IsFinite(CornerRounding) ? Math.Clamp(CornerRounding, 0f, 16f) : 6f;
        SidebarWidth = float.IsFinite(SidebarWidth) ? Math.Clamp(SidebarWidth, 120f, 480f) : 200f;
        CustomColours ??= [];
        if (!Enum.IsDefined(Theme)) Theme = ThemeMode.Dalamud;
        if (!GameThemes.IsKnown(GameThemeOverride)) GameThemeOverride = -1;
        if (!Enum.IsDefined(MessageStyle)) MessageStyle = MessageStyle.Bubbles;
        if (!Enum.IsDefined(Timestamps)) Timestamps = TimestampStyle.Dividers;
        if (!Enum.IsDefined(Font)) Font = FontChoice.Dalamud;
        if (!Enum.IsDefined(DtrMode)) DtrMode = DtrMode.Auto;
        if (!Enum.IsDefined(LastGroup)) LastGroup = ChannelGroup.Tell;
    }
}
