using System.Text;

namespace Parley.Core.Text;

/// <summary>
/// A stretch of a conversation picked out with the mouse, from one message to
/// another. Each end is a message and a position in that message's text.
///
/// Messages are held by reference rather than by number, because older
/// messages page in above them and change every number while the selection is
/// being made.
/// </summary>
public sealed class TextSelection
{
    public ChatMessage? AnchorMessage { get; private set; }
    public int AnchorIndex { get; private set; }
    public ChatMessage? FocusMessage { get; private set; }
    public int FocusIndex { get; private set; }

    public bool IsEmpty =>
        AnchorMessage == null || FocusMessage == null || (ReferenceEquals(AnchorMessage, FocusMessage) && AnchorIndex == FocusIndex);

    public void Clear()
    {
        AnchorMessage = null;
        FocusMessage = null;
        AnchorIndex = FocusIndex = 0;
    }

    public void Start(ChatMessage message, int index)
    {
        AnchorMessage = FocusMessage = message;
        AnchorIndex = FocusIndex = index;
    }

    public void Select(ChatMessage message, int start, int end)
    {
        AnchorMessage = FocusMessage = message;
        AnchorIndex = start;
        FocusIndex = end;
    }

    public void Extend(ChatMessage message, int index)
    {
        if (AnchorMessage == null)
        {
            Start(message, index);
            return;
        }

        FocusMessage = message;
        FocusIndex = index;
    }

    /// <summary>
    /// Both ends as positions in a list of messages, first to last. False if
    /// either end's message is no longer in the list, which clears it.
    /// </summary>
    public bool Resolve(IReadOnlyList<ChatMessage> messages, out Range range)
    {
        range = default;
        if (IsEmpty) return false;

        var anchor = IndexOf(messages, AnchorMessage!);
        var focus = ReferenceEquals(AnchorMessage, FocusMessage) ? anchor : IndexOf(messages, FocusMessage!);
        if (anchor < 0 || focus < 0)
        {
            Clear();
            return false;
        }

        var forward = anchor < focus || (anchor == focus && AnchorIndex <= FocusIndex);
        range = forward
            ? new Range(anchor, AnchorIndex, focus, FocusIndex)
            : new Range(focus, FocusIndex, anchor, AnchorIndex);
        return true;
    }

    /// <summary>The selected text, message by message, one line each.</summary>
    public string Copy(IReadOnlyList<ChatMessage> messages, Func<ChatMessage, RichText> textOf)
    {
        if (!Resolve(messages, out var range)) return string.Empty;

        var builder = new StringBuilder();
        for (var i = range.FirstMessage; i <= range.LastMessage; i++)
        {
            var text = textOf(messages[i]);
            range.Slice(i, text.Text.Length, out var start, out var end, out _);
            if (i > range.FirstMessage) builder.Append('\n');
            builder.Append(text.Copy(start, end));
        }
        return builder.ToString();
    }

    private static int IndexOf(IReadOnlyList<ChatMessage> messages, ChatMessage message)
    {
        // From the end: what is being selected is nearly always recent.
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(messages[i], message)) return i;
        }
        return -1;
    }

    /// <summary>A selection with its ends in order.</summary>
    public readonly record struct Range(int FirstMessage, int FirstIndex, int LastMessage, int LastIndex)
    {
        /// <summary>
        /// How much of message number <paramref name="message"/> is selected.
        /// False if none of it. <paramref name="continues"/> is set when the
        /// selection carries on into a later message.
        /// </summary>
        public bool Slice(int message, int length, out int start, out int end, out bool continues)
        {
            start = end = 0;
            continues = false;
            if (message < FirstMessage || message > LastMessage) return false;

            start = message == FirstMessage ? Math.Clamp(FirstIndex, 0, length) : 0;
            end = message == LastMessage ? Math.Clamp(LastIndex, 0, length) : length;
            continues = message < LastMessage;
            return end > start || continues;
        }
    }
}
