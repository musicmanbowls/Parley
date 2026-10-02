using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core;
using Parley.Core.Theme;

namespace Parley.Ui;

internal sealed partial class MainWindow
{
    /// <summary>Generous on purpose: a long draft is split into several messages rather than refused while typing.</summary>
    private const int MaxDraftLength = 2000;

    private const long NoticeMs = 6000;

    /// <summary>
    /// How many frames a request to place the caret stays good for. Focus
    /// asked for in one frame arrives in the next; the spare is in case it is
    /// a frame late, and the limit is so that a request which never takes
    /// effect does not lie in wait for the next time the box is clicked.
    /// </summary>
    private const int CaretFrames = 3;

    private readonly ImGui.ImGuiInputTextCallbackDelegate placeCaret;
    private int caretFrames;

    /// <summary>Where to put the caret when the box next takes focus, in UTF-8 bytes; -1 for the end.</summary>
    private int caretTarget = -1;

    /// <summary>Where the caret was the last time the box had focus, and in which conversation's box.</summary>
    private int caretBytes = -1;
    private string? caretOwner;
    private string? caretDrawing;

    /// <summary>Set by Enter: the keyboard goes back to the game at the end of this frame.</summary>
    private bool releaseKeyboard;

    private string composerNotice = string.Empty;
    private long composerNoticeUntil;

    /// <summary>
    /// Runs inside the reply box on every frame it has focus.
    ///
    /// ImGui treats focus given by code like tabbing into a field and selects
    /// everything in it, so the next key typed would replace a draft that was
    /// waiting there. A chat box wants the caret after the draft instead, or
    /// just after whatever was inserted. It also notes where the caret is, so
    /// a symbol picked from the menu goes in there.
    /// </summary>
    private int PlaceCaret(scoped ref ImGuiInputTextCallbackData data)
    {
        if (data.EventFlag == ImGuiInputTextFlags.CallbackHistory)
        {
            RecallSent(ref data);
            return 0;
        }

        if (data.EventFlag == ImGuiInputTextFlags.CallbackCompletion)
        {
            StartAutoTranslate(ref data);
            return 0;
        }

        if (caretFrames > 0)
        {
            caretFrames = 0;
            var at = caretTarget >= 0 ? Math.Min(caretTarget, data.BufTextLen) : data.BufTextLen;
            caretTarget = -1;
            data.CursorPos = at;
            data.SelectionStart = at;
            data.SelectionEnd = at;
        }

        caretBytes = data.CursorPos;
        caretOwner = caretDrawing;
        return 0;
    }

    /// <summary>Flags for every reply box: the caret is placed by hand, and Up and Down go through what was sent.</summary>
    private const ImGuiInputTextFlags ReplyBoxFlags = ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackHistory | ImGuiInputTextFlags.CallbackCompletion;

    // Where Up and Down have got to in what was sent: an index into the
    // plugin's list, or -1 for what was being typed, kept aside meanwhile.
    private int recallAt = -1;
    private string recallSaved = string.Empty;
    private string? recallOwner;

    /// <summary>
    /// Up and Down in a reply box, as in the game's chat box: back through the
    /// lines sent from any of Parley's boxes, and forward again to what was
    /// being typed.
    /// </summary>
    private void RecallSent(ref ImGuiInputTextCallbackData data)
    {
        var sent = plugin.SentHistory;
        if (sent.Count == 0) return;

        if (!string.Equals(recallOwner, caretDrawing, StringComparison.Ordinal))
        {
            recallOwner = caretDrawing;
            recallAt = -1;
        }

        string text;
        if (data.EventKey == ImGuiKey.UpArrow)
        {
            if (recallAt < 0)
            {
                recallSaved = Encoding.UTF8.GetString(data.BufTextSpan);
                recallAt = sent.Count - 1;
            }
            else if (recallAt > 0)
            {
                recallAt--;
            }
            else
            {
                return;
            }
            text = sent[Math.Min(recallAt, sent.Count - 1)];
        }
        else if (data.EventKey == ImGuiKey.DownArrow)
        {
            if (recallAt < 0) return;
            recallAt++;
            if (recallAt >= sent.Count)
            {
                recallAt = -1;
                text = recallSaved;
            }
            else
            {
                text = sent[recallAt];
            }
        }
        else
        {
            return;
        }

        data.DeleteChars(0, data.BufTextLen);
        data.InsertChars(0, text);
    }

    /// <summary>A line has gone: it joins what Up brings back, and Up starts again from the newest.</summary>
    private void Sent(string text)
    {
        plugin.RecordSent(text);
        recallAt = -1;
    }

    /// <summary>Height the reply box will take this frame, so the message list can leave exactly that much.</summary>
    private float ComposerHeight()
    {
        var style = ImGui.GetStyle();
        var height = ImGui.GetFrameHeight() + style.ItemSpacing.Y;
        if (selected != null && ComposerStatus(selected, out _, out _)) height += ImGui.GetTextLineHeight() + style.ItemSpacing.Y;
        return height;
    }

    private void DrawComposer(Conversation conversation)
    {
        var canSend = store.HasCharacter && conversation.CanSend && (!conversation.IsTell || conversation.WorldName.Length > 0);
        var hint = canSend
            ? ComposerHint(conversation)
            : !store.HasCharacter ? "Log in to send messages."
            : conversation.IsTell ? "This player's world is not known, so a tell cannot be addressed."
            : conversation.Group == ChannelGroup.FreeCompany ? "You are no longer in this free company."
            : "You are no longer in this linkshell.";

        canReply = canSend;

        var style = ImGui.GetStyle();
        var buttonSize = ImGui.GetFrameHeight();
        var inputWidth = MathF.Max(40f * scale, ImGui.GetContentRegionAvail().X - (buttonSize * 3f) - (style.ItemSpacing.X * 3f));

        if (focusComposer != null && (focusComposer == WhicheverIsShown || focusComposer == conversation.Key))
        {
            focusComposer = null;
            if (canSend)
            {
                ImGui.SetKeyboardFocusHere();
                caretFrames = CaretFrames;
            }
        }

        // Enter is detected by hand rather than with EnterReturnsTrue: with
        // that flag the binding decodes the box into a new string on every
        // frame, whether or not anything was typed.
        //
        // Each conversation has a reply box of its own as far as ImGui is
        // concerned. A box that is being typed in keeps its own copy of the
        // text and writes it back every frame, so if the same box carried on
        // across a switch of conversation (Alt+R does that without a click),
        // it would write one conversation's draft into the next.
        var draft = conversation.Draft;
        bool entered;
        ImGui.PushID(conversation.Key);
        try
        {
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.BeginDisabled(!canSend);
            caretDrawing = conversation.Key;
            ImGui.InputTextWithHint("##compose", hint, ref draft, MaxDraftLength, ReplyBoxFlags, placeCaret);
            entered = ImGui.IsItemDeactivated()
                      && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter));
            ImGui.EndDisabled();
        }
        finally
        {
            ImGui.PopID();
        }

        if (caretFrames > 0) caretFrames--;

        // ImGui treats Escape in a text box as "undo everything typed since it
        // was focused". In a chat box that throws away a message in progress,
        // so whatever Escape did to the text this frame is not accepted.
        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) draft = conversation.Draft;
        if (!string.Equals(draft, conversation.Draft, StringComparison.Ordinal))
        {
            store.SetDraft(conversation, draft);
            composerNotice = string.Empty;
        }

        ImGui.SameLine();
        DrawAutoTranslateButton(conversation, canSend, buttonSize);

        ImGui.SameLine();
        DrawSymbolButton(conversation, canSend, buttonSize);

        ImGui.SameLine();
        var ready = canSend && !string.IsNullOrWhiteSpace(conversation.Draft);
        var clicked = Painter.IconButton("##send", FontAwesomeIcon.PaperPlane, ready ? "Send" : string.Empty, palette, buttonSize,
            ready ? ColourMath.LegibleOn(palette.Accent, palette.WindowBg, palette.Text) : null, ready);

        // Alt+Enter is Parley's "bring me here to type" key. Pressed while
        // already typing here it has nothing to do: it neither sends nor lets
        // go of the keyboard.
        if (entered && ImGui.GetIO().KeyAlt)
        {
            entered = false;
            focusComposer = conversation.Key;
        }

        if ((entered || clicked) && ready) Submit(conversation, entered);
        else if (entered) AfterEnter(conversation);

        if (ComposerStatus(conversation, out var status, out var colour))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, colour);
            ImGui.TextUnformatted(Painter.Fit(status, ImGui.GetContentRegionAvail().X));
            ImGui.PopStyleColor();
        }
    }

    /// <summary>The grey text in an empty reply box, which also warns when a tell is unlikely to be seen.</summary>
    private string ComposerHint(Conversation conversation)
    {
        if (conversation.IsTell && config.ShowFriendStatus)
        {
            var status = plugin.FriendStatus(conversation.Title, conversation.WorldId);
            if (status.IsFriend && !status.Online) return $"{conversation.Title} is offline, so a tell will not reach them";
            if (status.Busy) return $"{conversation.Title} is busy and may not see a tell";
            if (status.InDuty) return $"{conversation.Title} is in a duty and may not see a tell";
        }
        return $"Message {conversation.Title}";
    }

    private void Submit(Conversation conversation, bool byEnter)
    {
        switch (plugin.Send(conversation, conversation.Draft))
        {
            case SendOutcome.Sent:
                Sent(conversation.Draft);
                store.SetDraft(conversation, string.Empty);
                composerNotice = string.Empty;

                // Having said something, go back to following the conversation.
                scroll.ToEnd();
                seekUnread = false;
                break;

            case SendOutcome.TooLong:
                ShowComposerNotice(config.SplitLongMessages
                    ? "Too long to send, even split into several messages."
                    : "Too long for one message. Shorten it, or turn on splitting in the settings.");
                break;

            case SendOutcome.CannotSend:
                ShowComposerNotice("This conversation cannot be written to right now.");
                break;
        }

        if (byEnter) AfterEnter(conversation);
        else focusComposer = conversation.Key;
    }

    /// <summary>
    /// Enter was pressed in the reply box. Like the game's own chat box, that
    /// is the end of typing: the keyboard goes back to the game, so moving and
    /// shortcuts work again without clicking somewhere else first. With that
    /// setting off, the cursor stays put for the next message instead.
    /// </summary>
    private void AfterEnter(Conversation conversation)
    {
        if (config.ReleaseKeyboardOnEnter) releaseKeyboard = true;
        else focusComposer = conversation.Key;
    }

    /// <summary>Called at the end of the frame: lets go of the keyboard if Enter asked for that.</summary>
    private void ReleaseKeyboardIfAsked()
    {
        if (!releaseKeyboard) return;
        releaseKeyboard = false;
        focusComposer = null;
        ImGuiP.FocusWindow(default);

        // Still the chat in use, though: the next Enter comes back here.
        plugin.UsingChat(this);
    }

    /// <summary>
    /// Puts text into a conversation's draft where the caret last was, or at
    /// the end, and gives the box focus with the caret just after it. With no
    /// conversation, it goes into General's box instead.
    /// </summary>
    private void InsertIntoDraft(Conversation? conversation, string text)
    {
        var key = conversation?.Key ?? GeneralKey;
        var draft = conversation?.Draft ?? generalDraft;
        var at = draft.Length;
        if (caretOwner != null && string.Equals(caretOwner, key, StringComparison.Ordinal) && caretBytes >= 0)
            at = CharIndex(draft, caretBytes);

        // A space between the insertion and a word it would otherwise run into.
        if (at > 0 && !char.IsWhiteSpace(draft[at - 1]) && text.Length > 1 && !char.IsWhiteSpace(text[0])) text = " " + text;

        var updated = draft.Insert(at, text);
        if (Encoding.UTF8.GetByteCount(updated) > (conversation == null ? GeneralMaxBytes : MaxDraftLength)) return;

        if (conversation == null) generalDraft = updated;
        else store.SetDraft(conversation, updated);
        caretTarget = Encoding.UTF8.GetByteCount(updated.AsSpan(0, at + text.Length));
        caretBytes = caretTarget;
        focusComposer = key;
    }

    /// <summary>The character index at a UTF-8 byte offset into a string, as ImGui counts the caret.</summary>
    private static int CharIndex(string text, int bytes)
    {
        var count = 0;
        var i = 0;
        while (i < text.Length && count < bytes)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                count += 4;
                i += 2;
            }
            else
            {
                count += text[i] < 0x80 ? 1 : text[i] < 0x800 ? 2 : 3;
                i++;
            }
        }
        return i;
    }

    private void ShowComposerNotice(string text)
    {
        composerNotice = text;
        composerNoticeUntil = Environment.TickCount64 + NoticeMs;
    }

    /// <summary>The line under the reply box, when there is something to say: a refusal, or how a long draft will go out.</summary>
    private bool ComposerStatus(Conversation conversation, out string text, out Vector4 colour)
    {
        if (composerNotice.Length > 0 && Environment.TickCount64 < composerNoticeUntil)
        {
            text = composerNotice;
            colour = palette.Error;
            return true;
        }

        text = string.Empty;
        colour = palette.TextMuted;
        if (conversation.Draft.Length == 0 || !conversation.CanSend) return false;

        var bytes = MessageSplitter.ByteCount(conversation.Draft) + (plugin.Outgoing.ExtraBytes?.Invoke(conversation.Draft) ?? 0);
        var budget = OutgoingQueue.Budget(conversation);
        if (bytes > budget)
        {
            if (!config.SplitLongMessages)
            {
                text = $"{bytes - budget} over the limit for one message.";
                colour = palette.Error;
                return true;
            }

            var split = MessageSplitter.Split(conversation.Draft, budget - (bytes - MessageSplitter.ByteCount(conversation.Draft)));
            text = split.TooLong ? "Too long to send, even split into several messages." : $"Will be sent as {split.Parts.Count} messages.";
            if (split.TooLong) colour = palette.Error;
            return true;
        }

        if (bytes > budget * 0.8f)
        {
            text = $"{budget - bytes} left";
            return true;
        }

        return false;
    }
}
