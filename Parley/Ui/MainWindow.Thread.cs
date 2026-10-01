using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core;
using Parley.Core.Settings;
using Parley.Core.Text;
using Parley.Core.Theme;

namespace Parley.Ui;

internal sealed partial class MainWindow
{
    /// <summary>A pause this long earns a centred timestamp before the next message.</summary>
    private const long DividerGapMs = 10 * 60 * 1000;

    /// <summary>Messages from one person closer together than this are drawn as one run, with the name shown once.</summary>
    private const long RunGapMs = 3 * 60 * 1000;

    private const long TooltipDelayMs = 600;

    /// <summary>When looking for last frame's newest message, how far back from the end to look.</summary>
    private const int NewMessageScan = 64;

    /// <summary>The conversation the list showed last frame. A different one now means starting again at its newest message.</summary>
    private Conversation? viewConversation;

    /// <summary>The newest message as of last frame. Whatever follows it this frame has just arrived.</summary>
    private ChatMessage? viewNewest;

    /// <summary>Where the list is scrolled to, measured from its newest message.</summary>
    private ListScroll scroll;

    /// <summary>Where on the scrollbar's thumb it was taken hold of, so it does not jump under the cursor.</summary>
    private float scrollGrab;

    /// <summary>Messages that have arrived below the visible part of the list while it was scrolled up.</summary>
    private int viewNewBelow;

    /// <summary>Set on opening a conversation with unread messages: go to the first of them once its history is in.</summary>
    private bool seekUnread;

    /// <summary>True while the cursor is over a control drawn on top of the messages, which then leave it alone.</summary>
    private bool pointerTaken;

    // Bumped whenever anything that affects text measurement changes, which
    // is what invalidates the sizes cached on each message.
    private int layoutStamp = 1;
    private int layoutKey;

    private ChatMessage? hoverMessage;
    private long hoverSince;
    private string? hoverTooltip;
    private ChatMessage? menuMessage;
    private bool linkHovered;
    private bool richFailureLogged;

    // Style as the window has it, captured before any region narrows it, so
    // menus opened from inside those regions are not cramped.
    private Vector2 normalPadding;
    private Vector2 normalSpacing;

    // The header's text, fitted to its width.
    private FitCache headerTitle;
    private FitCache headerSubtitle;
    private Conversation? subtitleFor;
    private int subtitleSlot = -1;
    private string subtitle = string.Empty;
    private Conversation? tellSubtitleFor;
    private FriendStatus tellSubtitleFriend;
    private string tellSubtitle = string.Empty;

    private readonly struct Metrics
    {
        public bool Bubbles { get; init; }
        public bool EveryTime { get; init; }
        public float Gutter { get; init; }
        public float PadX { get; init; }
        public float PadY { get; init; }
        public float Wrap { get; init; }
        public float NoticeWrap { get; init; }
        public float RunGap { get; init; }
        public float TightGap { get; init; }
        public float DividerHeight { get; init; }
        public float UnreadHeight { get; init; }
        public float HeaderHeight { get; init; }
        public float NoticePad { get; init; }
        public float Rounding { get; init; }
    }

    /// <summary>What one message occupies in the list, including anything drawn above it.</summary>
    private struct Block
    {
        public bool Divider;
        public bool Unread;
        public bool NewRun;
        public bool Header;
        public Vector2 TextSize;
        public float Height;
    }

    private void RefreshLayoutStamp()
    {
        var style = ImGui.GetStyle();
        normalPadding = style.WindowPadding;
        normalSpacing = style.ItemSpacing;

        var key = HashCode.Combine(ImGui.GetFontSize(), (int)config.MessageStyle, (int)config.Timestamps, fonts.Generation, config.Use24Hour);
        if (key == layoutKey) return;
        layoutKey = key;
        layoutStamp++;
        objectSizes.Clear();
    }

    // ------------------------------------------------------------------
    // Pane and header
    // ------------------------------------------------------------------

    private void DrawPane(float height)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var visible = ImGui.BeginChild("##pane", new Vector2(0f, height), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();

        try
        {
            if (!visible) return;

            if (selected == null)
            {
                DrawHeaderBar(null);
                DrawCentred(group switch
                {
                    ChannelGroup.Tell => "No conversation selected.\nStart one with the + button above, or wait for someone to write.",
                    ChannelGroup.Linkshell => "Your linkshells appear here once you are in one.",
                    ChannelGroup.FreeCompany => "You are not in a free company.\nIts chat appears here once you are.",
                    _ => "Your cross-world linkshells appear here once you are in one.",
                }, ImGui.GetContentRegionAvail());
            }
            else
            {
                var conversation = selected;
                DrawHeaderBar(conversation);
                var messagesHeight = MathF.Max(40f * scale, ImGui.GetContentRegionAvail().Y - ComposerHeight());
                DrawMessages(conversation, messagesHeight);
                DrawComposer(conversation);
            }
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    private void DrawHeaderBar(Conversation? conversation)
    {
        var drawList = ImGui.GetWindowDrawList();
        var lineHeight = ImGui.GetTextLineHeight();
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = (lineHeight * 2f) + (12f * scale);

        ImGui.Dummy(new Vector2(width, height));
        var after = ImGui.GetCursorScreenPos();
        var max = new Vector2(min.X + width, min.Y + height);
        drawList.AddRectFilled(min, max, Painter.U32(palette.HeaderBg), 6f * scale);

        var button = lineHeight + (10f * scale);
        var buttonY = min.Y + ((height - button) * 0.5f);

        var textLeft = min.X + (12f * scale);
        if (showSidebar)
        {
            ImGui.SetCursorScreenPos(new Vector2(min.X + (4f * scale), buttonY));
            var collapsed = config.SidebarCollapsed;
            if (Painter.IconButton("##collapse", collapsed ? FontAwesomeIcon.AngleDoubleRight : FontAwesomeIcon.AngleDoubleLeft,
                    collapsed ? "Show the conversation list" : "Shrink the conversation list", palette, button))
            {
                config.SidebarCollapsed = !collapsed;
                plugin.SaveConfig();
            }

            textLeft = min.X + (4f * scale) + button + (8f * scale);
        }

        var textRight = max.X - (8f * scale);

        if (conversation != null)
        {
            var x = max.X - (4f * scale) - button;
            ImGui.SetCursorScreenPos(new Vector2(x, buttonY));
            if (Painter.IconButton("##more", FontAwesomeIcon.EllipsisV, "More", palette, button)) ImGui.OpenPopup("##panemenu");
            if (BeginMenu("##panemenu"))
            {
                try
                {
                    DrawConversationMenuItems(conversation);
                }
                finally
                {
                    EndMenu();
                }
            }

            var highlight = ColourMath.LegibleOn(palette.Accent, palette.HeaderBg, palette.Text);

            x -= button + (2f * scale);
            ImGui.SetCursorScreenPos(new Vector2(x, buttonY));
            if (Painter.IconButton("##mute", conversation.Muted ? FontAwesomeIcon.BellSlash : FontAwesomeIcon.Bell,
                    conversation.Muted ? "Muted: new messages here are not counted.\nClick to unmute." : "Mute this conversation",
                    palette, button, conversation.Muted ? highlight : null))
                store.SetMuted(conversation, !conversation.Muted);

            x -= button + (2f * scale);
            ImGui.SetCursorScreenPos(new Vector2(x, buttonY));
            if (Painter.IconButton("##pin", FontAwesomeIcon.Thumbtack,
                    conversation.Pinned ? "Pinned to the top of the list.\nClick to unpin." : "Pin to the top of the list",
                    palette, button, conversation.Pinned ? highlight : null))
                store.SetPinned(conversation, !conversation.Pinned);

            textRight = x - (8f * scale);

            var top = min.Y + (6f * scale);
            var available = textRight - textLeft;
            drawList.AddText(new Vector2(textLeft, top), Painter.U32(palette.Text),
                headerTitle.Get(conversation.Title, available, layoutStamp, out _));
            drawList.AddText(new Vector2(textLeft, top + lineHeight),
                Painter.U32(ColourMath.LegibleOn(theme.ColourFor(conversation), palette.HeaderBg, palette.Text)),
                headerSubtitle.Get(Subtitle(conversation), available, layoutStamp, out _));
        }
        else
        {
            drawList.AddText(new Vector2(textLeft, min.Y + ((height - lineHeight) * 0.5f)), Painter.U32(palette.TextMuted),
                headerTitle.Get(group.Label(), textRight - textLeft, layoutStamp, out _));
        }

        ImGui.SetCursorScreenPos(after);
    }

    /// <summary>The line under a conversation's name. Built when the conversation or its slot changes, not per frame.</summary>
    private string Subtitle(Conversation conversation)
    {
        if (conversation.IsTell)
        {
            var world = conversation.WorldName.Length > 0 ? conversation.WorldName : "Unknown world";
            var friend = config.ShowFriendStatus ? plugin.FriendStatus(conversation.Title, conversation.WorldId) : FriendStatus.None;
            if (!friend.IsFriend) return world;
            if (ReferenceEquals(tellSubtitleFor, conversation) && tellSubtitleFriend == friend) return tellSubtitle;

            tellSubtitleFor = conversation;
            tellSubtitleFriend = friend;
            tellSubtitle = friend.InDuty && !friend.Busy && friend.Place.Length > 0
                ? $"{world} · Friend · In a duty: {friend.Place}"
                : $"{world} · Friend · {friend.Describe(conversation.WorldId)}";
            return tellSubtitle;
        }
        if (ReferenceEquals(subtitleFor, conversation) && subtitleSlot == conversation.Slot) return subtitle;

        subtitleFor = conversation;
        subtitleSlot = conversation.Slot;
        subtitle = conversation.Slot > 0 ? conversation.Group.SlotLabel(conversation.Slot) : "You are no longer a member";
        return subtitle;
    }

    // ------------------------------------------------------------------
    // Message list
    // ------------------------------------------------------------------

    private void DrawMessages(Conversation conversation, float height)
    {
        store.EnsureHistory(conversation);

        // The list does its own scrolling (see DrawMessageList), so ImGui's
        // is switched off for this child.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f * scale, 6f * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        var visible = ImGui.BeginChild("##messages", new Vector2(0f, height), false,
            ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        try
        {
            if (visible) DrawMessageList(conversation);
        }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
        }
    }

    /// <summary>
    /// Lays the conversation out by hand and scrolls it by hand.
    ///
    /// Every block's height is known before anything is drawn, so the list is
    /// positioned from its end: the newest message sits on the bottom edge and
    /// <see cref="scroll"/> says how far up from there the view has been
    /// moved. Only the blocks that reach into the view are drawn.
    ///
    /// ImGui's own scrolling is not used because it lags a frame behind the
    /// content. A new message, a change of conversation or a page of history
    /// would each show for one frame in the wrong place before settling.
    /// </summary>
    private void DrawMessageList(Conversation conversation)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var view = ImGui.GetContentRegionAvail();
        var barWidth = ImGui.GetStyle().ScrollbarSize;

        // The scrollbar's column is always set aside, shown or not, so text
        // does not wrap differently the moment there is enough to scroll.
        var width = view.X - barWidth - (4f * scale);
        if (width < 80f * scale || view.Y < 20f * scale) return;

        // Nothing below reads ImGui's scroll position and everything assumes
        // it is zero. Keyboard navigation can still move it; put it back.
        if (ImGui.GetScrollY() != 0f) ImGui.SetScrollY(0f);

        var messages = conversation.Messages;
        if (!ReferenceEquals(viewConversation, conversation))
        {
            viewConversation = conversation;
            viewNewest = null;
            viewNewBelow = 0;
            scroll.ToEnd();
            seekUnread = conversation.Unread > 0 && conversation.FirstUnreadTimestamp != 0;
            hoverMessage = null;
            menuMessage = null;
            selection.Clear();
            selecting = false;
        }

        BeginText(conversation);

        // While the view follows the newest message, dropping the oldest ones
        // from memory moves nothing on screen, so this is the time to do it.
        if (scroll.Following && seekKey == null) store.Trim(conversation);

        var metrics = BuildMetrics(width);
        var count = messages.Count;
        var loading = conversation.History == HistoryState.Loading;
        var loadRow = !conversation.ReachedStart || loading ? ImGui.GetFrameHeight() + (10f * scale) : 0f;

        // Find last frame's newest message. Anything after it has arrived
        // since, which only matters while the view is somewhere further up.
        var firstNew = count;
        if (!scroll.Following && viewNewest != null)
        {
            for (var i = count - 1; i >= 0 && i >= count - NewMessageScan; i--)
            {
                if (!ReferenceEquals(messages[i], viewNewest)) continue;
                firstNew = i + 1;
                break;
            }
        }
        viewNewest = count > 0 ? messages[count - 1] : null;

        // First pass: how tall everything is.
        var total = loadRow;
        var newHeight = 0f;
        var unreadTop = -1f;
        for (var i = 0; i < count; i++)
        {
            var block = Describe(conversation, i, metrics);
            if (block.Unread && unreadTop < 0f) unreadTop = total;
            if (i >= firstNew) newHeight += block.Height;
            total += block.Height;
        }
        total += 4f * scale;

        if (newHeight > 0f)
        {
            // Messages arrived while reading further up. The view stays on
            // what it was showing, and the button that leads back down says
            // how many there are.
            scroll.Append(newHeight);
            viewNewBelow += count - firstNew;
        }

        if (seekUnread && conversation.History == HistoryState.Loaded)
        {
            // Open on the first unread message when there are more of them
            // than fit, so they can be read in order. Fewer than that and
            // this leaves the view at the end, where they are all on screen.
            seekUnread = false;
            if (unreadTop >= 0f) scroll.ShowAtTop(unreadTop, total, view.Y);
        }

        if (ImGui.IsWindowHovered())
        {
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0f)
            {
                // Scrolling up with nothing further up to show asks for the
                // page before. It does nothing if there is none, or one is
                // already on its way.
                if (wheel > 0f && scroll.Back >= ListScroll.MaxBack(total, view.Y)) store.LoadOlder(conversation);

                // The same step ImGui takes for one notch of the wheel.
                scroll.Wheel(wheel, MathF.Floor(MathF.Min(5f * ImGui.GetFontSize(), view.Y * 0.67f)));
            }
        }

        scroll.Clamp(total, view.Y);
        if (total > view.Y)
        {
            DrawScrollbar(drawList, new Vector2(origin.X + view.X - barWidth, origin.Y), barWidth, view.Y, total);
            scroll.Clamp(total, view.Y);
        }

        if (scroll.Following) viewNewBelow = 0;

        // The way back down sits on top of the messages. It is hit-tested
        // before they are, because the first item under the cursor is the one
        // that gets the click, and painted after them, because the last thing
        // drawn is the one on top.
        var jump = !scroll.Following && count > 0;
        var jumpSize = ImGui.GetFrameHeight() + (4f * scale);
        var jumpAt = new Vector2(origin.X + view.X - barWidth - jumpSize - (10f * scale), origin.Y + view.Y - jumpSize - (6f * scale));
        var jumpHovered = false;
        var jumpHeld = false;
        if (jump)
        {
            ImGui.SetCursorScreenPos(jumpAt);
            if (ImGui.InvisibleButton("##latest", new Vector2(jumpSize, jumpSize)))
            {
                scroll.ToEnd();
                viewNewBelow = 0;
            }
            jumpHovered = ImGui.IsItemHovered();
            jumpHeld = ImGui.IsItemActive();
        }
        pointerTaken = jumpHovered;

        // Second pass: draw what reaches into the view.
        var clipTop = ImGui.GetWindowPos().Y;
        var clipBottom = clipTop + ImGui.GetWindowSize().Y;
        var y = MathF.Floor(origin.Y + scroll.ContentTop(total, view.Y));

        if (loadRow > 0f)
        {
            if (y + loadRow > clipTop && y < clipBottom) DrawLoadRow(conversation, new Vector2(origin.X, y), width, loadRow, loading);
            y += loadRow;
        }

        if (count == 0 && !loading) DrawEmptyThread(drawList, conversation, origin.X, width, origin.Y, view.Y);

        ChatMessage? hovered = null;
        linkHovered = false;
        for (var i = 0; i < count && y < clipBottom; i++)
        {
            var block = Describe(conversation, i, metrics);
            if (y + block.Height > clipTop
                && DrawBlock(drawList, conversation, i, new Vector2(origin.X, y), width, metrics, block)
                && !pointerTaken)
                hovered = messages[i];
            y += block.Height;
        }

        HandleTextMouse(conversation, origin, width, view.Y);
        SeekMessage(conversation, width, view.Y, total);

        if (jump && !scroll.Following) PaintJumpToLatest(drawList, jumpAt, jumpSize, jumpHovered, jumpHeld);

        ImGui.SetCursorScreenPos(origin);
        HandleMessageHover(hovered);
        DrawMessageMenu(conversation);
        DrawLinkMenu();
        DrawItemMenu(conversation);
    }

    /// <summary>
    /// The list's scrollbar, in the colours and rounding ImGui would give its
    /// own. Drag the thumb, or press anywhere on the track to bring the thumb
    /// there.
    /// </summary>
    private void DrawScrollbar(ImDrawListPtr drawList, Vector2 trackMin, float barWidth, float trackHeight, float total)
    {
        // The track is as tall as the view, so the two are the same number here.
        var thumb = ListScroll.ThumbHeight(trackHeight, total, trackHeight, 24f * scale);
        if (thumb >= trackHeight) return;

        ImGui.SetCursorScreenPos(trackMin);
        ImGui.InvisibleButton("##scroll", new Vector2(barWidth, trackHeight));
        var held = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();

        var thumbTop = scroll.ThumbTop(trackHeight, thumb, total, trackHeight);
        if (held)
        {
            var mouse = ImGui.GetIO().MousePos.Y - trackMin.Y;
            if (ImGui.IsItemActivated())
            {
                // A press on the thumb keeps hold of the point that was pressed.
                // A press on the bare track brings the thumb's middle to it.
                scrollGrab = mouse >= thumbTop && mouse <= thumbTop + thumb ? mouse - thumbTop : thumb * 0.5f;
            }

            scroll.DragThumb(mouse - scrollGrab, trackHeight, thumb, total, trackHeight);
            thumbTop = scroll.ThumbTop(trackHeight, thumb, total, trackHeight);
        }

        var rounding = ImGui.GetStyle().ScrollbarRounding;
        var inset = MathF.Max(1f, MathF.Floor(barWidth * 0.15f));
        var grab = held ? ImGuiCol.ScrollbarGrabActive : hovered ? ImGuiCol.ScrollbarGrabHovered : ImGuiCol.ScrollbarGrab;
        var top = trackMin.Y + thumbTop;
        drawList.AddRectFilled(trackMin, new Vector2(trackMin.X + barWidth, trackMin.Y + trackHeight), ImGui.GetColorU32(ImGuiCol.ScrollbarBg), rounding);
        drawList.AddRectFilled(
            new Vector2(trackMin.X + inset, top), new Vector2(trackMin.X + barWidth - inset, top + thumb),
            ImGui.GetColorU32(grab), rounding);
    }

    private void DrawEmptyThread(ImDrawListPtr drawList, Conversation conversation, float left, float width, float viewTop, float viewHeight)
    {
        var wrap = width * 0.8f;
        var position = new Vector2(left + (width * 0.1f), viewTop + (viewHeight * 0.4f));
        if (conversation.IsTell)
        {
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), position, Painter.U32(palette.TextMuted),
                $"This is the start of your conversation with {conversation.Title}.", wrap);
        }
        else
        {
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), position, Painter.U32(palette.TextMuted),
                "Nothing has been said here yet.", wrap);
        }
    }

    /// <summary>
    /// Paints the round button in the corner of the list that returns to the
    /// newest message, with a count of what has arrived since. The button
    /// itself was placed earlier in the frame; see the caller.
    /// </summary>
    private void PaintJumpToLatest(ImDrawListPtr drawList, Vector2 position, float size, bool hovered, bool held)
    {
        var centre = position + new Vector2(size * 0.5f, size * 0.5f);
        var fill = palette.HeaderBg with { W = 1f };
        if (hovered) fill = ColourMath.Shift(fill, held ? 0.16f : 0.08f);

        drawList.AddCircleFilled(centre, size * 0.5f, Painter.U32(fill), 24);
        drawList.AddCircle(centre, size * 0.5f, Painter.U32(palette.Border), 24);

        var glyph = Painter.IconSize(FontAwesomeIcon.ArrowDown);
        Painter.Icon(drawList, centre - (glyph * 0.5f), FontAwesomeIcon.ArrowDown, hovered ? palette.Text : palette.TextMuted);

        if (viewNewBelow > 0)
            Painter.Badge(drawList, new Vector2(position.X + size + (4f * scale), position.Y + (2f * scale)), viewNewBelow, palette, scale);

        if (hovered)
        {
            if (viewNewBelow > 0) ImGui.SetTooltip($"Jump to the newest message ({viewNewBelow} new)");
            else ImGui.SetTooltip("Jump to the newest message");
        }
    }

    private Metrics BuildMetrics(float width)
    {
        var bubbles = config.MessageStyle == MessageStyle.Bubbles;
        var everyTime = config.Timestamps == TimestampStyle.EveryMessage;
        var lineHeight = ImGui.GetTextLineHeight();
        var gutter = 2f * scale;
        var padX = bubbles ? 10f * scale : 0f;
        var padY = bubbles ? 6f * scale : 0f;

        // In bubble layout a time beside every message needs room left for it.
        var timeWidth = bubbles && everyTime
            ? ImGui.CalcTextSize(config.Use24Hour ? "00:00" : "00:00 AM").X + (8f * scale)
            : 0f;
        var widest = bubbles
            ? MathF.Min((width - timeWidth - gutter) * 0.84f, 640f * scale)
            : width - (gutter * 2f);

        return new Metrics
        {
            Bubbles = bubbles,
            EveryTime = everyTime,
            Gutter = gutter,
            PadX = padX,
            PadY = padY,
            Wrap = MathF.Max(40f * scale, widest - (padX * 2f)),
            NoticeWrap = MathF.Max(40f * scale, width * 0.8f),
            RunGap = bubbles ? 10f * scale : 8f * scale,
            TightGap = bubbles ? 2f * scale : 1f * scale,
            DividerHeight = lineHeight + (12f * scale),
            UnreadHeight = lineHeight + (8f * scale),
            HeaderHeight = lineHeight + (2f * scale),
            NoticePad = 3f * scale,
            Rounding = 10f * scale,
        };
    }

    private Block Describe(Conversation conversation, int index, in Metrics metrics)
    {
        var message = conversation.Messages[index];
        var previous = index > 0 ? conversation.Messages[index - 1] : null;
        var block = default(Block);

        var dayChanged = previous == null || LocalDay(previous) != LocalDay(message);
        block.Divider = dayChanged || (!metrics.EveryTime && message.Timestamp - previous!.Timestamp > DividerGapMs);

        var marker = conversation.FirstUnreadTimestamp;
        block.Unread = marker != 0 && !message.IsOutgoing && !message.IsNotice && message.Timestamp >= marker
                       && (previous == null || previous.Timestamp < marker);

        block.NewRun = previous == null || block.Divider || block.Unread
                       || previous.IsNotice || message.IsNotice
                       || previous.IsOutgoing != message.IsOutgoing
                       || previous.SenderWorld != message.SenderWorld
                       || !string.Equals(previous.Sender, message.Sender, StringComparison.Ordinal)
                       || message.Timestamp - previous.Timestamp > RunGapMs
                       // The flat layout has nowhere else to put a time, so
                       // "on every message" means a header on every message.
                       || (!metrics.Bubbles && metrics.EveryTime);

        // In bubbles, who is speaking only needs saying in a group chat, and
        // never for the local player, whose messages sit on the other side.
        block.Header = block.NewRun && !message.IsNotice
                       && (!metrics.Bubbles || (!conversation.IsTell && !message.IsOutgoing));

        block.TextSize = MeasureText(message, message.IsNotice ? metrics.NoticeWrap : metrics.Wrap);

        var height = block.NewRun ? metrics.RunGap : metrics.TightGap;
        if (block.Divider) height += metrics.DividerHeight;
        if (block.Unread) height += metrics.UnreadHeight;
        if (block.Header) height += metrics.HeaderHeight;
        height += message.IsNotice
            ? block.TextSize.Y + (metrics.NoticePad * 2f)
            : block.TextSize.Y + (metrics.PadY * 2f);
        block.Height = height;
        return block;
    }

    private static int LocalDay(ChatMessage message)
    {
        if (message.LocalDay == 0)
            message.LocalDay = (int)(TimeText.ToLocal(message.Timestamp).Date.Ticks / TimeSpan.TicksPerDay);
        return message.LocalDay;
    }

    /// <summary>"14:05" for a message, built once per clock format.</summary>
    private string Clock(ChatMessage message)
    {
        if (message.ClockText == null || message.ClockStamp != layoutStamp)
        {
            message.ClockText = TimeText.Clock(message.Timestamp, config.Use24Hour);
            message.ClockStamp = layoutStamp;
        }
        return message.ClockText;
    }

    /// <summary>"Today 14:05" for the divider above a message. Rebuilt when the date rolls over, since "today" moves.</summary>
    private string DividerLabel(ChatMessage message)
    {
        if (message.DividerText == null || message.DividerStamp != layoutStamp || message.DividerDay != today)
        {
            message.DividerText = TimeText.Divider(message.Timestamp, now, config.Use24Hour);
            message.DividerStamp = layoutStamp;
            message.DividerDay = today;
        }
        return message.DividerText;
    }

    private void ReportRichFailure(Exception ex)
    {
        if (richFailureLogged) return;
        richFailureLogged = true;
        Services.Log.Warning(ex, "Could not draw part of a formatted message; it is left out.");
    }

    private void DrawLoadRow(Conversation conversation, Vector2 position, float width, float height, bool loading)
    {
        if (loading)
        {
            const string label = "Loading…";
            var size = ImGui.CalcTextSize(label);
            ImGui.GetWindowDrawList().AddText(
                new Vector2(position.X + ((width - size.X) * 0.5f), position.Y + ((height - size.Y) * 0.5f)),
                Painter.U32(palette.TextMuted), label);
            return;
        }

        const string text = "Load earlier messages";
        var buttonWidth = ImGui.CalcTextSize(text).X + (ImGui.GetStyle().FramePadding.X * 2f);
        ImGui.SetCursorScreenPos(new Vector2(position.X + ((width - buttonWidth) * 0.5f), position.Y + (4f * scale)));
        if (ImGui.Button(text)) store.LoadOlder(conversation);
    }

    /// <returns>True if the cursor is over the message itself.</returns>
    private bool DrawBlock(ImDrawListPtr drawList, Conversation conversation, int index, Vector2 topLeft, float width, in Metrics metrics, in Block block)
    {
        var message = conversation.Messages[index];
        var y = topLeft.Y + (block.NewRun ? metrics.RunGap : metrics.TightGap);

        if (block.Divider)
        {
            DrawDivider(drawList, topLeft.X, y, width, metrics.DividerHeight, DividerLabel(message), palette.TextMuted);
            y += metrics.DividerHeight;
        }

        if (block.Unread)
        {
            DrawDivider(drawList, topLeft.X, y, width, metrics.UnreadHeight, "New", palette.Badge);
            y += metrics.UnreadHeight;
        }

        if (message.IsNotice)
        {
            var left = topLeft.X + ((width - block.TextSize.X) * 0.5f);
            DrawMessageText(drawList, message, index, new Vector2(left, y + metrics.NoticePad), metrics.NoticeWrap,
                message.IsError ? palette.Error : palette.Notice, palette.WindowBg with { W = 1f });
            return ImGui.IsMouseHoveringRect(new Vector2(left, y), new Vector2(left + block.TextSize.X, y + block.TextSize.Y + (metrics.NoticePad * 2f)));
        }

        if (ReferenceEquals(message, flashMessage)) DrawFlash(drawList, topLeft.X, y, width, metrics, block);

        return metrics.Bubbles
            ? DrawBubble(drawList, conversation, message, index, topLeft.X, y, width, metrics, block)
            : DrawLogEntry(drawList, conversation, message, index, topLeft.X, y, width, metrics, block);
    }

    private bool DrawBubble(ImDrawListPtr drawList, Conversation conversation, ChatMessage message, int index, float left, float y, float width, in Metrics metrics, in Block block)
    {
        var outgoing = message.IsOutgoing;
        var size = new Vector2(block.TextSize.X + (metrics.PadX * 2f), block.TextSize.Y + (metrics.PadY * 2f));
        var x = outgoing ? left + width - size.X - metrics.Gutter : left + metrics.Gutter;

        if (block.Header)
        {
            DrawSender(drawList, conversation, message, new Vector2(x + (2f * scale), y));
            y += metrics.HeaderHeight;
        }

        var min = new Vector2(x, y);
        var max = min + size;
        var fill = outgoing ? palette.BubbleOut : palette.BubbleIn;
        drawList.AddRectFilled(min, max, Painter.U32(fill), metrics.Rounding);
        DrawMessageText(drawList, message, index, new Vector2(min.X + metrics.PadX, min.Y + metrics.PadY), metrics.Wrap,
            outgoing ? palette.BubbleOutText : palette.BubbleInText, fill with { W = 1f });

        if (metrics.EveryTime)
        {
            var stamp = Clock(message);
            var stampSize = ImGui.CalcTextSize(stamp);
            var stampX = outgoing ? min.X - stampSize.X - (6f * scale) : max.X + (6f * scale);
            drawList.AddText(new Vector2(stampX, max.Y - stampSize.Y - (2f * scale)), Painter.U32(palette.TextMuted), stamp);
        }

        return ImGui.IsMouseHoveringRect(min, max);
    }

    private bool DrawLogEntry(ImDrawListPtr drawList, Conversation conversation, ChatMessage message, int index, float left, float y, float width, in Metrics metrics, in Block block)
    {
        var x = left + metrics.Gutter;
        if (block.Header)
        {
            var nameWidth = DrawSender(drawList, conversation, message, new Vector2(x, y));
            drawList.AddText(new Vector2(x + nameWidth + (8f * scale), y), Painter.U32(palette.TextMuted), Clock(message));
            y += metrics.HeaderHeight;
        }

        DrawMessageText(drawList, message, index, new Vector2(x, y), metrics.Wrap, palette.Text, palette.WindowBg with { W = 1f });
        return ImGui.IsMouseHoveringRect(new Vector2(left, y), new Vector2(left + width, y + block.TextSize.Y));
    }

    /// <summary>Draws who is speaking, and returns how wide that came out.</summary>
    private float DrawSender(ImDrawListPtr drawList, Conversation conversation, ChatMessage message, Vector2 position)
    {
        var outgoing = message.IsOutgoing;
        var name = outgoing ? "You" : message.Sender;
        var colour = ColourMath.LegibleOn(SenderColour(conversation, message) with { W = 1f }, palette.WindowBg, palette.Text);

        var nameSize = ImGui.CalcTextSize(name);
        drawList.AddText(position, Painter.U32(colour), name);
        var drawn = nameSize.X;

        if (outgoing || conversation.IsTell || message.SenderWorld == 0) return drawn;

        // In a group chat a name is a way to reach that person directly. The
        // click itself is handled with the rest of the list's mouse handling.
        nameSpots.Add(new NameSpot(position, new Vector2(position.X + nameSize.X, position.Y + nameSize.Y), message.Sender, message.SenderWorld, colour));

        // Members of a cross-world linkshell come from different worlds.
        if (conversation.Group == ChannelGroup.CrossWorld)
        {
            var world = plugin.Worlds.Name(message.SenderWorld);
            if (world.Length > 0)
            {
                // Interpolated straight into ImGui's own string type, so
                // nothing is allocated for a label drawn every frame.
                drawList.AddText(new Vector2(position.X + drawn, position.Y), Painter.U32(palette.TextMuted), $" · {world}");
                drawn += ImGui.CalcTextSize($" · {world}").X;
            }
        }

        return drawn;
    }

    private void DrawDivider(ImDrawListPtr drawList, float left, float y, float width, float height, string label, Vector4 colour)
    {
        var size = ImGui.CalcTextSize(label);
        var middle = y + (height * 0.5f);
        var textLeft = left + ((width - size.X) * 0.5f);
        drawList.AddText(new Vector2(textLeft, middle - (size.Y * 0.5f)), Painter.U32(colour), label);

        var line = Painter.U32(ColourMath.WithAlpha(colour, colour.W * 0.35f));
        var inset = 8f * scale;
        if (textLeft - inset > left + inset)
        {
            drawList.AddLine(new Vector2(left + inset, middle), new Vector2(textLeft - inset, middle), line, 1f);
            drawList.AddLine(new Vector2(textLeft + size.X + inset, middle), new Vector2(left + width - inset, middle), line, 1f);
        }
    }

    // ------------------------------------------------------------------
    // Hovering and the per-message menu
    // ------------------------------------------------------------------

    private void HandleMessageHover(ChatMessage? hovered)
    {
        if (hovered == null || !ImGui.IsWindowHovered())
        {
            hoverMessage = null;
            return;
        }

        if (!ReferenceEquals(hovered, hoverMessage))
        {
            hoverMessage = hovered;
            hoverSince = Environment.TickCount64;
            hoverTooltip = null;
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && !rightClickTaken)
        {
            menuMessage = hovered;
            ImGui.OpenPopup("##messagemenu");
            return;
        }

        // The exact time, but only for a cursor that has come to rest, and
        // never on top of a link's own tooltip.
        if (linkHovered || Environment.TickCount64 - hoverSince < TooltipDelayMs) return;
        hoverTooltip ??= TimeText.Full(hovered.Timestamp, config.Use24Hour);
        ImGui.SetTooltip(hoverTooltip);
    }

    private void DrawMessageMenu(Conversation conversation)
    {
        if (!BeginMenu("##messagemenu")) return;

        try
        {
            var message = menuMessage;
            if (message == null)
            {
                ImGui.CloseCurrentPopup();
                return;
            }

            if (!selection.IsEmpty)
            {
                if (ImGui.MenuItem("Copy selected text", "Ctrl+C")) CopySelection();
                ImGui.Separator();
            }

            if (ImGui.MenuItem("Copy message")) ImGui.SetClipboardText(message.Text);

            if (!message.IsNotice)
            {
                var speaker = message.IsOutgoing ? plugin.LocalName : message.Sender;
                if (ImGui.MenuItem("Copy with name and time"))
                    ImGui.SetClipboardText($"[{TimeText.Clock(message.Timestamp, config.Use24Hour)}] {speaker}: {message.Text}");

                if (!message.IsOutgoing && !conversation.IsTell && message.SenderWorld != 0)
                {
                    ImGui.Separator();
                    if (ImGui.MenuItem($"Send a tell to {message.Sender}")) plugin.OpenTell(message.Sender, message.SenderWorld);
                    if (ImGui.MenuItem("Copy name"))
                        ImGui.SetClipboardText($"{message.Sender}@{plugin.Worlds.Name(message.SenderWorld)}");
                }

                if (message.IsOutgoing)
                {
                    if (ImGui.MenuItem("Your name colour…"))
                        AskForNameColour(conversation, plugin.LocalName, plugin.Worlds.Name(plugin.LocalWorldId), outgoing: true);
                }
                else if (message.Sender.Length > 0 && ImGui.MenuItem($"Name colour for {message.Sender}…"))
                {
                    var world = message.SenderWorld != 0 ? plugin.Worlds.Name(message.SenderWorld) : conversation.WorldName;
                    AskForNameColour(conversation, message.Sender, world, outgoing: false);
                }
            }

            // Every web address in the message, in case the one wanted is
            // awkward to click.
            var first = true;
            foreach (var link in Parsed(message).Links)
            {
                if (link is not { Kind: LinkKind.Url, Url: { } url }) continue;
                if (first) ImGui.Separator();
                first = false;
                ImGui.PushID(link.Start);
                if (ImGui.MenuItem($"Open {Host(url)}")) plugin.OpenUrl(url);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(url);
                if (ImGui.MenuItem("Copy link")) ImGui.SetClipboardText(url);
                ImGui.PopID();
            }
        }
        finally
        {
            EndMenu();
        }
    }

    // ------------------------------------------------------------------
    // Menus
    // ------------------------------------------------------------------

    /// <summary>
    /// Opens a popup with the window's own padding and spacing restored. The
    /// sidebar and message list narrow both, and a menu opened from inside
    /// them would inherit that.
    /// </summary>
    private bool BeginMenu(string id)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, normalPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, normalSpacing);
        if (ImGui.BeginPopup(id)) return true;

        ImGui.PopStyleVar(2);
        return false;
    }

    private static void EndMenu()
    {
        ImGui.EndPopup();
        ImGui.PopStyleVar(2);
    }
}
