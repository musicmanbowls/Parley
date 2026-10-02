using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;

namespace Parley.Game;

/// <summary>
/// Notices the game being asked to start a tell, as Send Tell on a player or
/// on the friend list does, so that while Parley stands in for the game's chat
/// the tell can start in Parley's box instead. The game does this through
/// RaptureShellModule.SetContextTellTarget just before opening its chat box,
/// which this hooks to note who the tell is for. Framework thread only.
/// </summary>
internal sealed unsafe class TellRequests : IDisposable
{
    /// <summary>A request is for the chat box that opens straight after it; anything older is stale.</summary>
    private const long FreshMs = 3000;

    private readonly Hook<RaptureShellModule.Delegates.SetContextTellTarget>? hook;
    private (string Name, string World, ushort WorldId, long At)? last;

    public TellRequests()
    {
        try
        {
            var address = RaptureShellModule.Addresses.SetContextTellTarget.Value;
            if (address == 0) return;
            hook = Services.GameInterop.HookFromAddress<RaptureShellModule.Delegates.SetContextTellTarget>(address, SetTarget);
            hook.Enable();
        }
        catch (Exception ex)
        {
            hook = null;
            Services.Log.Warning(ex, "Could not watch for Send Tell; with Parley as the chat, Send Tell will open an empty box.");
        }
    }

    public void Dispose() => hook?.Dispose();

    /// <summary>Who the game was last asked to start a tell with, if that was just now. Each request is handed out once.</summary>
    public (string Name, string World, ushort WorldId)? Take()
    {
        var request = last;
        last = null;
        if (request is not { } fresh || Environment.TickCount64 - fresh.At > FreshMs) return null;
        return (fresh.Name, fresh.World, fresh.WorldId);
    }

    // Called from inside the game's own handling: nothing may escape, and the
    // game's function runs exactly once whatever happens here.
    private bool SetTarget(RaptureShellModule* module, Utf8String* playerName, Utf8String* worldName, ushort worldId, ulong accountId, ulong contentId, ushort reason, bool setChatType)
    {
        try
        {
            var name = playerName != null ? playerName->ToString() : string.Empty;
            var world = worldName != null ? worldName->ToString() : string.Empty;
            if (name.Length > 0) last = (name, world, worldId, Environment.TickCount64);
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, "Could not read who a tell was for.");
        }

        return hook!.Original(module, playerName, worldName, worldId, accountId, contentId, reason, setChatType);
    }
}
