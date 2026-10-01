using System.Numerics;

namespace Parley.Core;

[Flags]
public enum MessageFlags : byte
{
    None = 0,

    /// <summary>Sent by the local player.</summary>
    Outgoing = 1,

    /// <summary>Written by Parley or the game about the conversation, not said by anyone in it.</summary>
    Notice = 2,

    /// <summary>A notice reporting that something went wrong, usually a message the game refused.</summary>
    Error = 4,
}

public sealed class ChatMessage
{
    /// <summary>Unix milliseconds, UTC, taken when the message reached this client.</summary>
    public long Timestamp;

    public MessageFlags Flags;

    /// <summary>Player name without the world. Empty for notices.</summary>
    public string Sender = string.Empty;

    /// <summary>World row id of the sender, or 0 when the game did not say.</summary>
    public ushort SenderWorld;

    public string Text = string.Empty;

    /// <summary>
    /// The message as an encoded SeString, kept only when it carries something
    /// plain text cannot: an item or map link, an icon, auto-translate. Null
    /// for ordinary text, which is nearly all of it.
    /// </summary>
    public byte[]? Rich;

    public bool IsOutgoing => (Flags & MessageFlags.Outgoing) != 0;
    public bool IsNotice => (Flags & MessageFlags.Notice) != 0;
    public bool IsError => (Flags & MessageFlags.Error) != 0;

    // Layout cache. Owned by whichever view last drew the message; never
    // persisted, and recomputed whenever the stamp or wrap width differs.
    public int LayoutStamp;
    public float LayoutWrap;
    public Vector2 TextSize;

    /// <summary>The message with its links and colours worked out, built the first time it is drawn.</summary>
    public Text.RichText? Parsed;

    /// <summary>The message laid out as of <see cref="LayoutStamp"/> and <see cref="LayoutWrap"/>.</summary>
    public Text.TextLayout? Layout;

    /// <summary>Days since the epoch in local time, worked out on first use. 0 until then.</summary>
    public int LocalDay;

    /// <summary>The time of day as text, in whichever clock format <see cref="ClockStamp"/> was current for.</summary>
    public string? ClockText;
    public int ClockStamp;

    /// <summary>The label of the time divider above this message, which also depends on what day it is now.</summary>
    public string? DividerText;
    public int DividerStamp;
    public int DividerDay;

    /// <summary>
    /// The colour picked for the sender, if any, as of name colour version
    /// <see cref="NameColourStamp"/>. Looked up once rather than every frame.
    /// </summary>
    public int NameColourStamp = -1;
    public bool HasNameColour;
    public Vector4 NameColour;
}
