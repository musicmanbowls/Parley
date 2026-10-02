namespace Parley.Core;

/// <summary>
/// How the game decides which of its chat tabs show a line. Each tab has a
/// table with a bit for every kind of line, set when the tab leaves that kind
/// out. The bit is picked by the line's kind together with who it is from and
/// to, packed the way the game packs them.
/// </summary>
internal static class ChatLogFilter
{
    /// <summary>The game has four chat tabs.</summary>
    public const int TabCount = 4;

    /// <summary>The kind of chat, without who it is from and to.</summary>
    public static int KindOf(ushort info) => info & 0x7F;

    /// <summary>The game's 16-bit line info: the kind in bits 0-6, the target in bits 7-10 and the source in bits 11-14.</summary>
    public static ushort Pack(int kind, int target = 0, int source = 0) =>
        (ushort)((kind & 0x7F) | ((target & 0xF) << 7) | ((source & 0xF) << 11));

    /// <summary>Whether a tab with this table shows the line. With no table, or one too short to say, it shows it.</summary>
    public static bool Shows(byte[]? hidden, ushort info)
    {
        if (hidden == null) return true;
        var at = info >> 3;
        return at >= hidden.Length || (hidden[at] & (1 << (info & 7))) == 0;
    }
}
