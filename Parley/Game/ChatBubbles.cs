using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Parley.Core;

namespace Parley.Game;

/// <summary>
/// Gives the game's chat bubbles the words of the lines Parley hides from the
/// chat log, over your own head and over the heads of people around you.
///
/// Having printed a line, the game shows it in a bubble over whoever said it,
/// taking the words from a copy made while printing. A hidden line is never
/// printed, so its bubble would repeat the last line that was, such as a
/// "has logged out" notice. The bubble is shown by a call to
/// RaptureLogModule.ShowMiniTalkPlayer straight after the print, which this
/// hooks to hand it the hidden line instead. Lines Parley does not hide are
/// left alone. Framework thread only, which is where the game handles chat.
/// </summary>
internal sealed unsafe class ChatBubbles : IDisposable
{
    private readonly BubbleText held = new();
    private readonly Hook<RaptureLogModule.Delegates.ShowMiniTalkPlayer>? hook;

    public ChatBubbles()
    {
        try
        {
            var address = RaptureLogModule.Addresses.ShowMiniTalkPlayer.Value;
            if (address == 0)
            {
                Services.Log.Warning("The game's chat bubble function was not found; bubbles for chats Parley hides may show the wrong words.");
                return;
            }

            hook = Services.GameInterop.HookFromAddress<RaptureLogModule.Delegates.ShowMiniTalkPlayer>(address, ShowBubble);
            hook.Enable();
        }
        catch (Exception ex)
        {
            hook = null;
            Services.Log.Warning(ex, "Could not attach to the game's chat bubbles; bubbles for chats Parley hides may show the wrong words.");
        }
    }

    public void Dispose() => hook?.Dispose();

    /// <summary>A line has arrived. Whatever was held for the last one is no longer wanted.</summary>
    public void LineArrived() => held.Forget();

    /// <summary>Parley is keeping this line out of the chat log; its bubble should still say it.</summary>
    public void Hiding(XivChatType kind, SeString message)
    {
        if (hook != null) held.Hold((ushort)kind, message.Encode());
    }

    // Called from inside the game's own chat handling: nothing may escape, and
    // the game's function is called exactly once whatever happens here.
    private void ShowBubble(RaptureLogModule* module, ushort logKindId, Utf8String* sender, Utf8String* message, ushort worldId, bool isLocalPlayer)
    {
        Utf8String* hidden = null;
        try
        {
            var words = held.Take(logKindId);
            if (words != null) hidden = Utf8String.FromSequence(words);
        }
        catch (Exception ex)
        {
            Services.Log.Verbose(ex, "Could not give a chat bubble the hidden line.");
        }

        try
        {
            // The game copies the words for itself, so these only have to outlive the call.
            hook!.Original(module, logKindId, sender, hidden != null ? hidden : message, worldId, isLocalPlayer);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "The game's chat bubble call failed.");
        }
        finally
        {
            if (hidden != null) hidden->Dtor(true);
        }
    }
}
