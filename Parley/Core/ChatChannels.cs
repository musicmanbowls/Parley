namespace Parley.Core;

/// <summary>
/// A channel the game's chat box can be set to: the game's number for it, a
/// name, the command that switches to it, and the kind of chat line it makes,
/// whose colour the box takes. Linkshell channels say which slot they are.
/// </summary>
internal sealed record ChatChannel(int Id, string Name, string Command, int LogKind, ChannelGroup? Group = null, int Slot = 0);

/// <summary>
/// The game's chat box channels, numbered as RaptureShellModule.ChatType
/// numbers them. Party being 2 was confirmed in game; the rest follow the same
/// numbering other chat plugins switch channels with.
/// </summary>
internal static class ChatChannels
{
    public const int TellId = 0;

    /// <summary>The kind of line a tell you send makes, for the colour of the box while it is on a tell.</summary>
    public const int TellLogKind = 12;

    public static readonly ChatChannel[] All = Build();

    /// <summary>The channel with the game's number, or null for one Parley does not know.</summary>
    public static ChatChannel? ById(int id)
    {
        foreach (var channel in All)
        {
            if (channel.Id == id) return channel;
        }
        return null;
    }

    private static ChatChannel[] Build()
    {
        var channels = new List<ChatChannel>
        {
            new(1, "Say", "/s", 10),
            new(4, "Yell", "/y", 30),
            new(5, "Shout", "/sh", 11),
            new(2, "Party", "/p", 14),
            new(3, "Alliance", "/a", 15),
            new(6, "Free Company", "/fc", 24, ChannelGroup.FreeCompany, 1),
            new(8, "Novice Network", "/beginner", 27),
        };

        for (var i = 1; i <= ChannelGroups.SlotsPerGroup; i++)
            channels.Add(new(18 + i, $"Linkshell {i}", $"/l{i}", 15 + i, ChannelGroup.Linkshell, i));

        // The first cross-world linkshell's kind of line sits on its own; the
        // other seven are a run further up, as in the chat log.
        for (var i = 1; i <= ChannelGroups.SlotsPerGroup; i++)
            channels.Add(new(8 + i, $"Cross-world Linkshell {i}", $"/cwl{i}", i == 1 ? 37 : 99 + i, ChannelGroup.CrossWorld, i));

        return [.. channels];
    }
}
