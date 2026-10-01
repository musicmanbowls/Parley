namespace Parley.Core;

/// <summary>
/// The words of a line Parley has just kept out of the game's chat log, held
/// for the chat bubble the game shows straight afterwards.
///
/// The game makes a bubble from its own copy of the last line it printed, so
/// a line that was never printed would get the previous line's words. A held
/// line goes to the next bubble only if that bubble is for the same kind of
/// chat, and it is dropped as soon as a bubble or another line comes along.
/// </summary>
internal sealed class BubbleText
{
    /// <summary>The kind of chat sits in the low bits; the rest say who sent it and to whom.</summary>
    private const ushort KindMask = 0x7F;

    private ushort kind;
    private byte[]? words;

    public bool Holding => words != null;

    public void Hold(ushort chatKind, byte[] text)
    {
        kind = (ushort)(chatKind & KindMask);
        words = text;
    }

    /// <summary>Another line has arrived, so whatever is held did not get a bubble.</summary>
    public void Forget() => words = null;

    /// <summary>The held words if this bubble is for them. Nothing is held afterwards either way.</summary>
    public byte[]? Take(ushort bubbleKind)
    {
        var held = words;
        words = null;
        return held != null && (bubbleKind & KindMask) == kind ? held : null;
    }
}
