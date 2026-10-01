using FFXIVClientStructs.FFXIV.Client.UI;

namespace Parley.Game;

internal static unsafe class GameSound
{
    /// <summary>The sixteen chat sound effects are consecutive; &lt;se.1&gt; is this plus one.</summary>
    private const uint ChatEffectBase = 36;

    /// <summary>Plays one of the sounds &lt;se.1&gt; to &lt;se.16&gt; make in chat. Framework thread only.</summary>
    public static void PlayChatEffect(int number)
    {
        if (number is < 1 or > 16) return;
        UIGlobals.PlaySoundEffect(ChatEffectBase + (uint)number);
    }
}
