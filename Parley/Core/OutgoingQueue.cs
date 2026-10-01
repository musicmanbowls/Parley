using System.Text;

namespace Parley.Core;

public enum SendOutcome
{
    Sent,
    Empty,

    /// <summary>The conversation cannot be written to: a linkshell the character is no longer in.</summary>
    CannotSend,

    /// <summary>Too long for one line and splitting is off, or too long even split.</summary>
    TooLong,
}

/// <summary>
/// Paces outgoing messages and works out which of them the game refused.
///
/// The game says nothing when a message is accepted except to echo it back
/// into the chat log, and nothing structured when it is refused: an error line
/// appears in the log and no echo follows. So each send is remembered until
/// its echo arrives, and an error line that turns up while one is still
/// waiting is taken to be about that send.
/// </summary>
public sealed class OutgoingQueue
{
    /// <summary>The game's limit on one chat line, command included, in UTF-8 bytes.</summary>
    public const int MaxLineBytes = 500;

    /// <summary>Stop waiting for an echo after this long. Nothing is reported; the send is simply forgotten.</summary>
    private const long EchoWindowMs = 10_000;

    /// <summary>An error line later than this after a send is about something else.</summary>
    private const long ErrorWindowMs = 4_000;

    /// <summary>Minimum spacing between two separate messages, so a quick pair does not reach the server as a burst.</summary>
    private const long MinGapMs = 350;

    private readonly Func<string, bool> sendLine;
    private readonly Func<long> clock;
    private readonly List<Pending> pending = [];
    private readonly LinkedList<Queued> queue = [];
    private long nextSendAt;
    private int nextBatch;

    /// <param name="sendLine">Hands one complete chat line to the game. False if the game would not take it.</param>
    public OutgoingQueue(Func<string, bool> sendLine, Func<long>? clock = null)
    {
        this.sendLine = sendLine;
        this.clock = clock ?? (() => Environment.TickCount64);
    }

    public bool SplitLongMessages { get; set; } = true;

    /// <summary>Gap between the parts of one split message.</summary>
    public int SplitDelayMs { get; set; } = 1100;

    /// <summary>Strips characters the game cannot send. Applied before measuring, so the budget is for what actually goes out.</summary>
    public Func<string, string>? Sanitise { get; set; }

    /// <summary>
    /// How many more bytes a text will take on the wire than its characters
    /// suggest: an item linked into a reply is a short stand-in in the text and
    /// a longer link when it is sent.
    /// </summary>
    public Func<string, int>? ExtraBytes { get; set; }

    /// <summary>
    /// A message did not go through. Arguments: the conversation, the text
    /// that was lost (every unsent part, rejoined), and the game's reason.
    /// </summary>
    public event Action<Conversation, string, string>? Failed;

    public int QueuedCount => queue.Count;
    public int PendingCount => pending.Count;

    public static string CommandPrefix(Conversation conversation) => conversation.IsTell
        ? $"/tell {conversation.Title}@{conversation.WorldName} "
        : $"{conversation.Group.SlotCommand(conversation.Slot)} ";

    /// <summary>Bytes left for the message itself once the command and target are paid for.</summary>
    public static int Budget(Conversation conversation) =>
        MaxLineBytes - Encoding.UTF8.GetByteCount(CommandPrefix(conversation));

    /// <summary>
    /// Checks a message and queues it. Nothing reaches the game here: that
    /// happens in <see cref="Update"/>, so the caller can be UI code while
    /// the game is only ever spoken to from the framework thread.
    /// </summary>
    public SendOutcome Send(Conversation conversation, string text)
    {
        var body = MessageSplitter.Normalise(Sanitise?.Invoke(text) ?? text);
        if (body.Length == 0) return SendOutcome.Empty;
        if (!conversation.CanSend || (conversation.IsTell && conversation.WorldName.Length == 0))
            return SendOutcome.CannotSend;

        var split = MessageSplitter.Split(body, Budget(conversation) - (ExtraBytes?.Invoke(body) ?? 0));
        if (split.TooLong || split.Parts.Count == 0) return SendOutcome.TooLong;
        if (split.Parts.Count > 1 && !SplitLongMessages) return SendOutcome.TooLong;

        var batch = nextBatch++;
        for (var i = 0; i < split.Parts.Count; i++)
            queue.AddLast(new Queued(conversation, split.Parts[i], batch, i > 0));

        return SendOutcome.Sent;
    }

    /// <summary>
    /// Call once per framework tick: sends the next line if one is due and
    /// forgets echoes that never came. At most one line goes out per call.
    /// </summary>
    public void Update()
    {
        Pump();
        if (pending.Count == 0) return;

        // A plain loop: this runs every frame, and a lambda capturing the
        // time would be allocated on every one of them.
        var now = clock();
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            if (now - pending[i].SentAt > EchoWindowMs) pending.RemoveAt(i);
        }
    }

    /// <summary>The game echoed one of the local player's messages into this conversation.</summary>
    public void OnEcho(Conversation conversation)
    {
        // Oldest first. The echoed text is not compared: the game may have
        // trimmed or substituted it, and order within a conversation is kept.
        for (var i = 0; i < pending.Count; i++)
        {
            if (!ReferenceEquals(pending[i].Conversation, conversation)) continue;
            pending.RemoveAt(i);
            return;
        }
    }

    /// <summary>
    /// The game printed an error line. Returns true if it was taken as the
    /// reason a recent send never echoed, in which case <see cref="Failed"/>
    /// has been raised.
    /// </summary>
    public bool OnGameError(string reason)
    {
        if (pending.Count == 0) return false;

        var latest = pending[^1];
        if (clock() - latest.SentAt > ErrorWindowMs) return false;

        pending.RemoveAt(pending.Count - 1);
        Failed?.Invoke(latest.Conversation, RejoinWithQueued(latest.Text, latest.Batch), reason);
        return true;
    }

    /// <summary>Drops everything waiting to go out, for logout.</summary>
    public void Clear()
    {
        queue.Clear();
        pending.Clear();
    }

    private void Pump()
    {
        if (queue.First is not { } node) return;

        var now = clock();
        if (now < nextSendAt) return;

        var item = node.Value;
        queue.RemoveFirst();

        if (!item.Conversation.CanSend)
        {
            var reason = item.Conversation.Group == ChannelGroup.FreeCompany
                ? "You are no longer in this free company."
                : "You are no longer in this linkshell.";
            Failed?.Invoke(item.Conversation, RejoinWithQueued(item.Text, item.Batch), reason);
            return;
        }

        if (!sendLine(CommandPrefix(item.Conversation) + item.Text))
        {
            Failed?.Invoke(item.Conversation, RejoinWithQueued(item.Text, item.Batch), "The game would not accept that message.");
            return;
        }

        pending.Add(new Pending(item.Conversation, item.Text, item.Batch, now));

        var next = queue.First?.Value;
        nextSendAt = now + (next is { Continuation: true } && next.Batch == item.Batch ? SplitDelayMs : MinGapMs);
    }

    /// <summary>Pulls the remaining parts of a failed message out of the queue and returns the whole unsent text.</summary>
    private string RejoinWithQueued(string first, int batch)
    {
        var builder = new StringBuilder(first);
        for (var node = queue.First; node != null;)
        {
            var next = node.Next;
            if (node.Value.Batch == batch)
            {
                builder.Append(' ').Append(node.Value.Text);
                queue.Remove(node);
            }
            node = next;
        }
        return builder.ToString();
    }

    private sealed record Queued(Conversation Conversation, string Text, int Batch, bool Continuation);

    private sealed record Pending(Conversation Conversation, string Text, int Batch, long SentAt);
}
