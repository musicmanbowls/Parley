using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Parley.Core;

namespace Parley.UiHarness;

/// <summary>A character with a believable amount of chat, for the scenarios to look at.</summary>
internal static class Demo
{
    public const ushort Jenova = 40;
    public const ushort Adamantoise = 73;
    public const ushort Cactuar = 79;
    public const ushort Faerie = 54;
    public const ushort Gilgamesh = 63;

    private const long Minute = 60_000;
    private const long Hour = 60 * Minute;
    private const long Day = 24 * Hour;

    public static void Seed(Stage stage)
    {
        var store = stage.Plugin.Store;
        var now = stage.Clock;
        store.LoadCharacter(0x0040000012345678, stage.Plugin.LocalName, "Jenova", saveHistory: false);

        store.SetSlots(ChannelGroup.Linkshell, ["Moonlit Anglers", "Hunt Train NA", "", "Static Planning", "", "", "", ""]);
        store.SetSlots(ChannelGroup.CrossWorld, ["Aether Hunts", "Crystal Market Watch", "", "", "", "", "", ""]);
        store.SetSlots(ChannelGroup.FreeCompany, ["Lanternlight Society"]);

        // ---- Tells ----
        var oskar = store.OpenTell("Oskar Lindqvist", Adamantoise, "Adamantoise");
        Theirs(store, oskar, now - (2 * Day) - (3 * Hour), "Oskar Lindqvist", Adamantoise, "gg, that last pull was clean");
        Mine(stage, oskar, now - (2 * Day) - (3 * Hour) + Minute, "it really was. same time thursday?");
        Theirs(store, oskar, now - (2 * Day) - (3 * Hour) + (2 * Minute), "Oskar Lindqvist", Adamantoise, "works for me");

        var yuna = store.OpenTell("Yuna Hoshizora", Jenova, "Jenova");
        Theirs(store, yuna, now - Day - (5 * Hour), "Yuna Hoshizora", Jenova, "do you still have that spare glamour prism?");
        Mine(stage, yuna, now - Day - (5 * Hour) + (4 * Minute), "I do! I'll mail it over");

        var tobias = store.OpenTell("Tobias Greywater", Cactuar, "Cactuar");
        Theirs(store, tobias, now - (6 * Hour), "Tobias Greywater", Cactuar, "WTB the crafted belt if you are still selling");

        var mira = store.OpenTell("Mira Thorne", Jenova, "Jenova");
        Theirs(store, mira, now - (95 * Minute), "Mira Thorne", Jenova, "hey, are you around tonight?");
        Mine(stage, mira, now - (93 * Minute), "yep, on after the reset. what's up?");
        Theirs(store, mira, now - (92 * Minute), "Mira Thorne", Jenova, "we need one more for the alliance raid");
        Theirs(store, mira, now - (92 * Minute) + 20_000, "Mira Thorne", Jenova, "healer or dps, either works. It should only be one run unless the loot goes badly, and then we will probably go again until somebody gets the chest piece.");
        Mine(stage, mira, now - (90 * Minute), "count me in. I'll bring scholar");
        Mine(stage, mira, now - (90 * Minute) + 15_000, "invite whenever");
        Theirs(store, mira, now - (41 * Minute), "Mira Thorne", Jenova, "forming up now, sending you an invite");
        Mine(stage, mira, now - (40 * Minute), "on my way");

        // ---- Linkshells ----
        var anglers = store.GetLinkshell(ChannelGroup.Linkshell, "Moonlit Anglers", 1);
        Theirs(store, anglers, now - (3 * Hour), "Pell Marrow", Jenova, "anyone know where the big fish window is tonight?");
        Theirs(store, anglers, now - (3 * Hour) + Minute, "Isolde Varn", Jenova, "Ruby Sea, about forty minutes from now");
        Theirs(store, anglers, now - (3 * Hour) + (2 * Minute), "Isolde Varn", Jenova, "bring the good bait this time");
        Mine(stage, anglers, now - (3 * Hour) + (3 * Minute), "I'll be there, save me a spot on the rocks");
        Theirs(store, anglers, now - (70 * Minute), "Pell Marrow", Jenova, "window is up!");
        Theirs(store, anglers, now - (69 * Minute), "Hana Birchwood", Jenova, "got it on the second cast, unbelievable");
        Theirs(store, anglers, now - (68 * Minute), "Isolde Varn", Jenova, "congratulations! that one took me three weeks");

        var hunts = store.GetLinkshell(ChannelGroup.Linkshell, "Hunt Train NA", 2);
        Theirs(store, hunts, now - (30 * Minute), "Conductor Reyes", Jenova, "DT train leaving Urqopacha in 5, instance 1");
        Theirs(store, hunts, now - (29 * Minute), "Conductor Reyes", Jenova, "please do not pull early");

        var planning = store.GetLinkshell(ChannelGroup.Linkshell, "Static Planning", 4);
        Theirs(store, planning, now - (8 * Hour), "Oskar Lindqvist", Adamantoise, "strat doc is updated for phase two");

        // A linkshell the character is no longer in: history with no slot.
        var old = store.GetLinkshell(ChannelGroup.Linkshell, "Old Friends", 5);
        Theirs(store, old, now - (9 * Day), "Bren Halloway", Jenova, "take care everyone");
        store.SetSlots(ChannelGroup.Linkshell, ["Moonlit Anglers", "Hunt Train NA", "", "Static Planning", "", "", "", ""]);

        // ---- Cross-world linkshells ----
        var aether = store.GetLinkshell(ChannelGroup.CrossWorld, "Aether Hunts", 1);
        Theirs(store, aether, now - (25 * Minute), "Saya Windrunner", Gilgamesh, "S rank up in Kholusia, coordinates in the usual place");
        Theirs(store, aether, now - (24 * Minute), "Dorian Fell", Faerie, "on my way, please hold for two minutes");
        Theirs(store, aether, now - (23 * Minute), "Saya Windrunner", Gilgamesh, "holding");
        Mine(stage, aether, now - (22 * Minute), "coming from Jenova, nearly there");

        var market = store.GetLinkshell(ChannelGroup.CrossWorld, "Crystal Market Watch", 2);
        Theirs(store, market, now - (4 * Hour), "Dorian Fell", Faerie, "materia prices dropped again overnight");

        // ---- Free company ----
        var company = store.GetLinkshell(ChannelGroup.FreeCompany, "Lanternlight Society", 1);
        Theirs(store, company, now - (5 * Hour), "Isolde Varn", Jenova, "morning all! workshop needs more timber for the new airship");
        Theirs(store, company, now - (5 * Hour) + Minute, "Pell Marrow", Jenova, "I can gather some after lunch");
        Mine(stage, company, now - (5 * Hour) + (3 * Minute), "I have a few stacks spare, dropping them in the chest now");
        Theirs(store, company, now - (5 * Hour) + (4 * Minute), "Isolde Varn", Jenova, "you are a star, thank you");

        // Everything so far has been seen. What follows has not.
        store.MarkAllRead();
        foreach (var conversation in store.All) store.ClearUnreadMarker(conversation);

        Theirs(store, tobias, now - (12 * Minute), "Tobias Greywater", Cactuar, "still interested if it's available!");
        Theirs(store, hunts, now - (3 * Minute), "Conductor Reyes", Jenova, "pulling the first mark now");
        Theirs(store, hunts, now - (2 * Minute), "Pell Marrow", Jenova, "here");
        Theirs(store, hunts, now - Minute, "Hana Birchwood", Jenova, "here too");
        Theirs(store, market, now - (9 * Minute), "Saya Windrunner", Gilgamesh, "crafted gear is selling well on Gilgamesh today");
        Theirs(store, company, now - (16 * Minute), "Hana Birchwood", Jenova, "FC map run at 9 if anyone wants in, we have two spots");
        Theirs(store, company, now - (15 * Minute), "Pell Marrow", Jenova, "count me in");
    }

    /// <summary>Fills a conversation with enough back-and-forth to need scrolling.</summary>
    public static void Pad(Stage stage, Conversation conversation, int count, string sender, ushort world, long startAgo = 10 * Hour)
    {
        var store = stage.Plugin.Store;
        var at = stage.Clock - startAgo;
        for (var i = 1; i <= count; i++)
        {
            at += 45_000;
            if (i % 4 == 0) Mine(stage, conversation, at, $"reply number {i}");
            else Theirs(store, conversation, at, sender, world, $"line number {i} of the conversation");
        }
        store.MarkRead(conversation);
        store.ClearUnreadMarker(conversation);
    }

    public static void Theirs(ConversationStore store, Conversation conversation, long at, string sender, ushort world, string text) =>
        store.Add(conversation, new ChatMessage { Timestamp = at, Sender = sender, SenderWorld = world, Text = text });

    /// <summary>
    /// A message with an item linked in it, encoded the way the game sends one:
    /// the rarity colour, the item, the name, the end of the link. The game's
    /// link marker glyph is left out; the stage's fonts do not have it.
    /// </summary>
    public static void TheirsWithItem(ConversationStore store, Conversation conversation, long at, string sender, ushort world,
        string before, uint itemId, string name, string after, ushort rarityColour = 549)
    {
        var payloads = new List<Payload>
        {
            new TextPayload(before),
            new UIForegroundPayload(rarityColour),
            new UIGlowPayload((ushort)(rarityColour + 1)),
            new ItemPayload(itemId, false),
            new TextPayload(name),
            RawPayload.LinkTerminator,
            new UIGlowPayload(0),
            new UIForegroundPayload(0),
            new TextPayload(after),
        };

        store.Add(conversation, new ChatMessage
        {
            Timestamp = at,
            Sender = sender,
            SenderWorld = world,
            Text = before + name + after,
            Rich = new SeString(payloads).Encode(),
        });
    }

    public static void Mine(Stage stage, Conversation conversation, long at, string text) =>
        stage.Plugin.Store.Add(conversation, new ChatMessage
        {
            Timestamp = at, Flags = MessageFlags.Outgoing, Sender = stage.Plugin.LocalName, SenderWorld = stage.Plugin.LocalWorldId, Text = text,
        });
}
