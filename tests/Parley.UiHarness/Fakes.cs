// Stand-ins for the parts of the plugin that need a running game. They carry
// the same names, in the same namespaces, as the real ones, so the window code
// compiles against them unchanged.

using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.ImGuiSeStringRenderer;
using Dalamud.Interface.Utility;
using Parley.Core;
using Parley.Core.History;
using Parley.Core.Settings;
using Parley.Core.Theme;
using Parley.Game;
using Parley.Ui;

namespace Parley
{
    internal static class Services
    {
        public static FakeLog Log { get; } = new();
        public static FakeTargets Targets { get; } = new();
        public static FakeGameGui GameGui { get; } = new();
    }

    internal sealed class FakeLog
    {
        public List<string> Problems { get; } = [];

        public void Error(Exception ex, string message) => Problems.Add($"ERROR {message} {ex}");
        public void Warning(Exception ex, string message) => Problems.Add($"WARN {message} {ex.GetType().Name}: {ex.Message}");
        public void Verbose(Exception ex, string message) { }
    }

    internal sealed class FakeTargets
    {
        public IGameObject? Target => null;
    }

    internal sealed class FakeGameGui
    {
        public bool OpenMapWithMapLink(MapLinkPayload map) => false;
    }

    internal sealed class FakeIpc
    {
        public bool RecentlyPolled { get; set; }
    }

    /// <summary>The plugin as the windows see it: a store, settings, and a game that echoes whatever it is sent.</summary>
    public sealed class Plugin
    {
        public Plugin(HistoryStore? history, Func<long> clock)
        {
            Config = new Configuration();
            Worlds = new WorldLookup();
            Store = new ConversationStore(history, clock) { ResolveWorld = Worlds.Id };

            // On the stage's clock, which moves a frame at a time however fast the frames are drawn.
            Outgoing = new OutgoingQueue(Echo, clock);
            Outgoing.Failed += (conversation, text, reason) =>
            {
                Store.AddNotice(conversation, $"Not sent: {reason}", error: true);
                if (conversation.Draft.Length == 0) Store.SetDraft(conversation, text);
            };

            GameColours = new GameColours();
            Theme = new ThemeManager(Config, GameColours);
            Fonts = new FontManager();
            MainWindow = new MainWindow(this);
            foreach (var group in ChannelGroups.All) PopOuts[(int)group] = new MainWindow(this, group);
            SettingsWindow = new SettingsWindow(this);
            ApplyConfig();
        }

        internal MainWindow[] PopOuts { get; } = new MainWindow[ChannelGroups.Count];

        internal IEnumerable<MainWindow> ChatWindows => [MainWindow, .. PopOuts];

        internal MainWindow WindowFor(ChannelGroup group) => Config.IsPoppedOut(group) ? PopOuts[(int)group] : MainWindow;

        internal void ShowConversation(Conversation conversation, bool focus = true) => WindowFor(conversation.Group).Show(conversation, focus);

        internal void ShowPopOut(ChannelGroup group)
        {
            if (Config.IsPoppedOut(group)) PopOuts[(int)group].Show();
        }

        internal void PopOut(ChannelGroup group)
        {
            Config.SetPoppedOut(group, true);
            var window = PopOuts[(int)group];
            window.SelectKey(group, MainWindow.SelectedKey(group));
            window.Show();
        }

        internal void DockBack(ChannelGroup group)
        {
            var window = PopOuts[(int)group];
            MainWindow.SelectKey(group, window.SelectedKey(group));
            Config.SetPoppedOut(group, false);
            window.IsOpen = false;
            var selected = window.SelectedKey(group) is { } key ? Store.Find(key) : null;
            if (selected != null) MainWindow.Show(selected);
        }

        /// <summary>The window that last handed the keyboard back with Enter, which the real plugin sends the next Enter to.</summary>
        internal MainWindow? ChatInUse { get; private set; }

        internal void UsingChat(MainWindow window) => ChatInUse = window;

        /// <summary>Every address the window asked to open in a browser.</summary>
        internal List<string> OpenedUrls { get; } = [];

        internal void OpenUrl(string url) => OpenedUrls.Add(url);

        /// <summary>Friends as a test sets them up, by name and home world.</summary>
        internal Dictionary<(string Name, ushort World), FriendStatus> Friends { get; } = [];

        internal FriendStatus FriendStatus(string name, ushort worldId) =>
            Config.ShowFriendStatus && Friends.TryGetValue((name, worldId), out var status) ? status : Core.FriendStatus.None;

        internal string ItemLinkToken(uint rawId, string name) => "" + name.Replace(' ', ' ');

        internal Configuration Config { get; }
        internal ConversationStore Store { get; }
        internal OutgoingQueue Outgoing { get; }
        internal WorldLookup Worlds { get; }
        internal GameColours GameColours { get; }
        internal ThemeManager Theme { get; }
        internal FontManager Fonts { get; }
        internal MainWindow MainWindow { get; }
        internal SettingsWindow SettingsWindow { get; }
        internal FakeIpc Ipc { get; } = new();

        internal string HistoryDirectory => @"C:\Users\you\AppData\Roaming\XIVLauncher\pluginConfigs\Parley\history";
        internal string LocalName { get; set; } = "Wren Alder";
        internal ushort LocalWorldId { get; set; } = 40;

        /// <summary>Every line handed to the game, in order.</summary>
        internal List<string> SentLines { get; } = [];

        /// <summary>When set, the game refuses the next line with this message.</summary>
        internal string? RefuseNext { get; set; }

        internal int SettingsOpened { get; private set; }
        internal int ConfigSaves { get; private set; }

        internal void Tick()
        {
            Store.Tick();
            Outgoing.Update();
        }

        internal void OpenSettings()
        {
            SettingsOpened++;
            SettingsWindow.IsOpen = true;
            SettingsWindow.BringToFront();
        }

        internal void OpenTell(string name, ushort worldId)
        {
            if (!Store.HasCharacter) return;
            ShowConversation(Store.OpenTell(name, worldId, Worlds.Name(worldId)));
        }

        internal SendOutcome Send(Conversation conversation, string text) =>
            Store.HasCharacter ? Outgoing.Send(conversation, text) : SendOutcome.CannotSend;

        // ---- General ----

        internal GeneralLog General { get; } = new();

        /// <summary>The game's number for the channel its chat box is on, as a test sets it. 2 is Party.</summary>
        internal int ChannelId { get; set; } = 2;

        internal string TellTo { get; set; } = string.Empty;

        /// <summary>Every line typed into General's box, in order.</summary>
        internal List<string> SentGeneral { get; } = [];

        internal (ChatChannel? Channel, string Label, string TellTo) CurrentChannel() =>
            ChannelId == ChatChannels.TellId ? (null, "/tell", TellTo) : (ChatChannels.ById(ChannelId), string.Empty, string.Empty);

        /// <summary>The default Log Text Colors of a few kinds of line; the rest have none, so fall back to the text colour.</summary>
        internal System.Numerics.Vector4? ChatColour(int kind) => kind switch
        {
            10 => Core.Theme.ColourMath.FromRgb(0xF7F7F7),
            12 or 13 => Core.Theme.ColourMath.FromRgb(0xFFB8DE),
            14 or 32 => Core.Theme.ColourMath.FromRgb(0x66E5FF),
            24 => Core.Theme.ColourMath.FromRgb(0xABDBE5),
            16 => Core.Theme.ColourMath.FromRgb(0xD4FF7D),
            56 => Core.Theme.ColourMath.FromRgb(0xCCCCCC),
            57 => Core.Theme.ColourMath.FromRgb(0xCCCCB2),
            61 => Core.Theme.ColourMath.FromRgb(0xABD647),
            _ => null,
        };

        // A little of the game's auto-translate dictionary.
        private static readonly AutoTranslateGroup[] AtGroups = [new(2, "Greetings"), new(8, "Tactics"), new(49, "Mounts")];
        private static readonly AutoTranslatePhrase[] AtPhrases =
        [
            new(2, 1, "Hello."), new(2, 2, "Good morning!"), new(2, 3, "Nice to meet you."), new(2, 4, "Thank you."),
            new(8, 1, "Pull together!"), new(8, 2, "Stack up!"), new(8, 3, "Spread out!"),
            new(49, 1, "Company Chocobo"), new(49, 2, "Midgardsormr"),
        ];

        internal IReadOnlyList<AutoTranslateGroup> AutoTranslateGroups() => AtGroups;
        internal IReadOnlyList<AutoTranslatePhrase> AutoTranslatePhrases(uint group) => AtPhrases.Where(phrase => phrase.Group == group).ToList();
        internal IReadOnlyList<AutoTranslatePhrase> AutoTranslateAll() => AtPhrases;
        internal string AutoTranslateToken(AutoTranslatePhrase phrase) => "" + phrase.Text.Replace(' ', ' ') + "";

        /// <summary>Whether Parley stands in for the game's chat, as a test sets it.</summary>
        internal bool ReplacingGameChat { get; set; }

        /// <summary>Whether the game's chat would be out of sight, as in a cutscene, as a test sets it.</summary>
        internal bool ChatHidden { get; set; }

        internal bool GameChatWouldHide() => ChatHidden;

        /// <summary>How often General's "+" asked the game for another tab, and its cog for the game's log settings.</summary>
        internal int TabsAsked { get; private set; }
        internal int LogSettingsOpened { get; private set; }

        /// <summary>Whether the game's chat log can be reached, as a test sets it. Without it "+" has nowhere to go.</summary>
        internal bool GameChatReachable { get; set; } = true;

        internal bool AddGameChatTab()
        {
            if (!GameChatReachable) return false;
            TabsAsked++;
            return true;
        }

        internal bool OpenGameLogSettings()
        {
            LogSettingsOpened++;
            return true;
        }

        internal void NoteSlowWork(string what, long started) { }

        internal List<string> SentHistory { get; } = [];

        internal void RecordSent(string text)
        {
            var line = text.Trim();
            if (line.Length > 0 && (SentHistory.Count == 0 || SentHistory[^1] != line)) SentHistory.Add(line);
        }

        internal bool SendGeneral(string text)
        {
            if (!Store.HasCharacter || string.IsNullOrWhiteSpace(text)) return false;
            SentGeneral.Add(text.Trim());
            return true;
        }

        internal void SaveConfig() => ConfigSaves++;

        internal void ApplyConfig()
        {
            Store.PageSize = Config.PageSize;
            Store.MaxLoadedMessages = Config.MaxLoadedMessages;
            Outgoing.SplitLongMessages = Config.SplitLongMessages;
            Outgoing.SplitDelayMs = Config.SplitDelayMs;
        }

        internal void ReloadCharacter() { }

        /// <summary>Sounds are not played here, only asked for.</summary>
        internal List<ChannelGroup> AlertsTested { get; } = [];

        internal string? TestAlert(ChannelGroup group)
        {
            AlertsTested.Add(group);
            return null;
        }

        /// <summary>
        /// Plays the game's part: takes a chat line, and puts the message it
        /// carries back into the log as the local player's own.
        /// </summary>
        private bool Echo(string line)
        {
            SentLines.Add(line);

            Conversation? target = null;
            string text;
            if (line.StartsWith("/tell ", StringComparison.Ordinal))
            {
                var at = line.IndexOf('@');
                var space = at < 0 ? -1 : line.IndexOf(' ', at);
                if (space < 0) return false;

                var name = line[6..at];
                var world = line[(at + 1)..space];
                text = line[(space + 1)..];
                target = Store.OpenTell(name, Worlds.Id(world), world);
            }
            else
            {
                var space = line.IndexOf(' ');
                if (space < 0) return false;

                var command = line[..space];
                text = line[(space + 1)..];
                foreach (var conversation in Store.All)
                {
                    if (conversation.IsTell || conversation.Slot == 0) continue;
                    if (conversation.Group.SlotCommand(conversation.Slot) == command) target = conversation;
                }
            }

            if (target == null) return false;

            if (RefuseNext is { } reason)
            {
                // The game takes the line and answers with an error instead of an echo.
                RefuseNext = null;
                Pending.Add(() => Outgoing.OnGameError(reason));
                return true;
            }

            var echoed = target;
            Pending.Add(() =>
            {
                Store.Add(echoed, new ChatMessage { Flags = MessageFlags.Outgoing, Sender = LocalName, SenderWorld = LocalWorldId, Text = text });
                Outgoing.OnEcho(echoed);
            });
            return true;
        }

        /// <summary>What the game will say back on the next frame.</summary>
        internal List<Action> Pending { get; } = [];

        internal void Deliver()
        {
            if (Pending.Count == 0) return;
            var work = Pending.ToArray();
            Pending.Clear();
            foreach (var item in work) item();
        }
    }
}

namespace Parley.Game
{
    /// <summary>The game's side of links in messages, recording what was asked of it.</summary>
    internal static class GameLinks
    {
        public static List<string> Calls { get; } = [];

        /// <summary>Items a test knows about, by their id without offsets.</summary>
        public static Dictionary<uint, ItemInfo> Items { get; } = new()
        {
            [5059] = new ItemInfo(5059, "Rose Gold Ingot", "An ingot of gold alloyed with copper.", 20807, false, false, false, 1, 50, 1, "Metal", false, true, true),
            [41090] = new ItemInfo(41090, "Augmented Quetzalli Gloves", "Gloves woven for a fight.", 47660, false, false, false, 4, 690, 100, "Hands", true, false, false),
        };

        public static ItemInfo? Item(uint rawId) =>
            Items.TryGetValue(ItemInfo.BaseId(rawId), out var item) ? item with { HighQuality = ItemInfo.IsHighQualityId(rawId) } : null;

        public static ImTextureID? Icon(uint icon, bool highQuality) => null;

        /// <summary>When set, the game's own tooltip "shows"; otherwise the window falls back to drawing one.</summary>
        public static bool NativeTooltips { get; set; }

        public static uint TooltipItem { get; private set; }

        public static bool ShowItemTooltip(uint rawId, object owner)
        {
            if (!NativeTooltips) return false;
            if (TooltipItem != rawId) Calls.Add($"tooltip {rawId}");
            TooltipItem = rawId;
            return true;
        }

        public static void HideItemTooltip(object owner)
        {
            if (TooltipItem != 0) Calls.Add("tooltip hidden");
            TooltipItem = 0;
        }

        public static void TryOn(uint rawId) => Calls.Add($"try on {rawId}");
        public static void Compare(uint rawId) => Calls.Add($"compare {rawId}");
        public static void FindItem(uint rawId) => Calls.Add($"find {rawId}");
        public static void SearchRecipes(uint itemId) => Calls.Add($"recipes {itemId}");
        public static void OpenPartyFinder(uint listingId) => Calls.Add($"party finder {listingId}");

        /// <summary>A few of the game's UI colours (dark theme column), enough for item and map links.</summary>
        public static uint UiColour(ushort key, int theme) => key switch
        {
            500 => 0xFF7B1AFF,
            501 => 0x000000FF,
            549 => 0xFFFFFFFF,
            551 => 0x5FD38BFF,
            553 => 0x5C9EFFFF,
            555 => 0xB47AFFFF,
            561 => 0xF49AC2FF,
            _ => 0,
        };
    }

    internal readonly record struct Friend(string Name, ushort WorldId, bool Online);

    internal static class FriendList
    {
        public static List<Friend> Friends { get; } =
        [
            new("Mira Thorne", 40, true),
            new("Kestrel Moss", 79, true),
            new("Oskar Lindqvist", 73, true),
            new("Ansel Brightwater", 54, false),
        ];

        public static List<Friend> Read() => [.. Friends];
    }

    internal sealed class WorldLookup
    {
        private static readonly (ushort Id, string Name)[] Known =
        [
            (40, "Jenova"), (73, "Adamantoise"), (79, "Cactuar"), (54, "Faerie"), (63, "Gilgamesh"), (99, "Sargatanas"), (65, "Midgardsormr"), (75, "Siren"),
        ];

        public string Name(uint id)
        {
            foreach (var world in Known)
            {
                if (world.Id == id) return world.Name;
            }
            return string.Empty;
        }

        public ushort Id(string name)
        {
            foreach (var world in Known)
            {
                if (string.Equals(world.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return world.Id;
            }
            return 0;
        }

        public bool IsPlayable(uint id) => Name(id).Length > 0;
    }

    /// <summary>The game's UI theme and chat colours, settable from a test.</summary>
    internal sealed class GameColours
    {
        private readonly Vector4[] local = new Vector4[ChannelGroups.SlotsPerGroup];
        private readonly Vector4[] cross = new Vector4[ChannelGroups.SlotsPerGroup];

        public GameColours()
        {
            Array.Fill(local, GameThemes.DefaultLinkshell);
            Array.Fill(cross, GameThemes.DefaultCrossWorld);
        }

        public int Theme { get; set; }
        public Vector4 Tell { get; set; } = GameThemes.DefaultTell;
        public Vector4 FreeCompany { get; set; } = GameThemes.DefaultFreeCompany;

        public void SetSlot(ChannelGroup group, int slot, uint rgb) =>
            (group == ChannelGroup.CrossWorld ? cross : local)[slot - 1] = ColourMath.FromRgb(rgb);

        public Vector4 Slot(ChannelGroup group, int slot)
        {
            if (group == ChannelGroup.FreeCompany) return FreeCompany;
            var index = Math.Clamp(slot - 1, 0, ChannelGroups.SlotsPerGroup - 1);
            return group == ChannelGroup.CrossWorld ? cross[index] : local[index];
        }

        public Vector4 Group(ChannelGroup group) => group == ChannelGroup.Tell ? Tell : Slot(group, 1);
    }
}

namespace Parley.Ui
{
    /// <summary>The default font is all there is here.</summary>
    internal sealed class FontManager : IDisposable
    {
        public int Generation { get; set; }

        public void Apply(Configuration config) { }

        public IDisposable? Push(float sectionScale = 1f) => null;

        public void Dispose() { }
    }

    /// <summary>
    /// Hides Dalamud's UiBuilder from the window code. The real one reaches
    /// for a Dalamud service that does not exist outside the game.
    /// </summary>
    internal static class UiBuilder
    {
        public static ImFontPtr IconFont { get; set; }
    }

    /// <summary>Hides Dalamud's ImGuiHelpers, for the same reason.</summary>
    internal static class ImGuiHelpers
    {
        public static float GlobalScale => ImGui.GetIO().FontGlobalScale;

        /// <summary>
        /// There is no SeString renderer outside the game. Only inline icons
        /// go through it, and here each is a box the size of a line.
        /// </summary>
        public static SeStringDrawResult SeStringWrapped(
            ReadOnlySpan<byte> text, scoped in SeStringDrawParams style = default, ImGuiId imGuiId = default, ImGuiButtonFlags buttonFlags = default)
        {
            var size = style.FontSize ?? ImGui.GetFontSize();
            if (style.TargetDrawList is { IsNull: false } drawList && style.ScreenOffset is { } at)
                drawList.AddRect(at, at + new Vector2(size, size), 0xFF8080FF, 3f);
            return new SeStringDrawResult { Size = new Vector2(size, size) };
        }
    }
}
