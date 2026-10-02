using System.Runtime.CompilerServices;
using Dalamud;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Parley.Core;

namespace Parley.Game;

/// <summary>
/// The game's chat tabs as the player has set them up, and the channel the
/// game's chat box is on. Framework thread only.
///
/// Tab names and the channel are documented in ClientStructs. Which lines a
/// tab shows is not: the game's print function (RaptureLogModule.PrintMessage)
/// tests a bit in a table each tab points to, and leaves out a line that every
/// tab in use has the bit set for. The offsets below come from reading that
/// function in the game's code. The table is copied with SafeMemory, so if a
/// patch moves it Parley shows every line rather than crashing.
/// </summary>
internal static unsafe class GameChatTabs
{
    public const int Count = ChatLogFilter.TabCount;

    // Inside each RaptureLogModuleTab: a pointer to the table of the kinds it
    // leaves out, and a value that is zero while the tab is not in use.
    private const int FilterOffset = 0x8E0;
    private const int InUseOffset = 0x8EC;

    /// <summary>Kind, target and source take 15 bits, so the table is at most 32768 bits long.</summary>
    private const int FilterBytes = 32768 / 8;

    /// <summary>Enough for every kind with no sender or target, should the whole table not be readable.</summary>
    private const int ShortFilterBytes = 16;

    public static List<GameChatTab> Read()
    {
        var tabs = new List<GameChatTab>(Count);
        var module = RaptureLogModule.Instance();
        if (module == null) return tabs;

        for (var i = 0; i < Count; i++)
        {
            ref var tab = ref module->ChatTabs[i];
            var at = (byte*)Unsafe.AsPointer(ref tab);
            var inUse = *(ushort*)(at + InUseOffset) != 0;
            var table = *(nint*)(at + FilterOffset);

            byte[]? hidden = null;
            if (table != 0 && !SafeMemory.ReadBytes(table, FilterBytes, out hidden)
                && !SafeMemory.ReadBytes(table, ShortFilterBytes, out hidden))
                hidden = null;

            tabs.Add(new GameChatTab(i, tab.Name.ToString(), inUse, hidden));
        }
        return tabs;
    }

    /// <summary>The channel the game's chat box sends to: its number, the label the game gives it, and who a tell goes to.</summary>
    public static (int Channel, string Label, string TellTo) CurrentChannel()
    {
        var shell = RaptureShellModule.Instance();
        if (shell == null) return (0, string.Empty, string.Empty);
        return (shell->ChatType, shell->CurrentChannel.ToString(), shell->TellName.ToString());
    }
}
