using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core;
using Parley.Core.Settings;
using Parley.Core.Theme;

namespace Parley.Ui;

/// <summary>
/// The conversations of the section on screen, to pick from: a row of tabs
/// across the top, as General has, or a list down the side. Which one is set
/// per section, apart for the main window and for a window of its own. Tabs
/// leave the conversation the window's whole width, which is what a small
/// main window needs; they scroll sideways when there are more than fit, and
/// can be cut down to pictures alone.
///
/// Neither shows who a conversation is with beyond a name. That, the world,
/// and a friend's whereabouts are in a banner that shows while a tab or row
/// is pointed at, so none of it takes room from the messages.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The longest a name on a tab gets before it is cut short, unscaled.</summary>
    private const float MaxTabText = 150f;

    /// <summary>How far one notch of the wheel moves the tabs, unscaled.</summary>
    private const float TabWheelStep = 60f;

    /// <summary>Where each kind's tabs are scrolled to, in pixels from the first.</summary>
    private readonly float[] tabScroll = new float[ChannelGroups.Count];

    /// <summary>The conversation last brought into view among the tabs. Each newly selected one is, once, and the wheel is free after.</summary>
    private string? tabShown;

    /// <summary>Whether the section on screen picks its conversations from tabs across the top, as set for this kind of window.</summary>
    private bool TabsAcross => look.TabsIn(poppedOut: only != null) == TabDirection.Horizontal;

    /// <summary>The conversations of the section on screen, with whichever way of picking them it has, and the one picked.</summary>
    private void DrawConversations(float height)
    {
        // A character is in one free company at most, so that tab only needs
        // a way to pick when there are old ones to pick from.
        var list = store.InGroup(group);
        var choosing = group != ChannelGroup.FreeCompany || list.Count > 1;
        showSidebar = choosing && !TabsAcross;

        if (showSidebar)
        {
            DrawSidebar(height);
            ImGui.SameLine(0f, 0f);
            DrawSplitter(height);
            ImGui.SameLine(0f, 0f);
        }
        else if (choosing && list.Count > 0)
        {
            var top = ImGui.GetCursorPosY();
            DrawConversationTabs(list);
            height -= ImGui.GetCursorPosY() - top;
        }

        DrawPane(height);
    }

    /// <summary>
    /// A tab for each conversation, newest or lowest slot first as in the
    /// list, scrolled with the wheel or the arrows when they do not all fit.
    /// At the right end, a button to show names or pictures only.
    /// </summary>
    private void DrawConversationTabs(IReadOnlyList<Conversation> list)
    {
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var iconsOnly = config.TabIconsOnly;
        var lineHeight = ImGui.GetTextLineHeight();
        var height = iconsOnly ? MathF.Max(lineHeight + (10f * scale), (TabPictureRadius(lineHeight) * 2f) + (6f * scale)) : lineHeight + (10f * scale);
        var gap = 2f * scale;
        var button = lineHeight + (6f * scale);

        // Reserve the strip as one item, then place the tabs over it.
        ImGui.Dummy(new Vector2(width, height));

        Span<float> widths = list.Count <= 128 ? stackalloc float[list.Count] : new float[list.Count];
        var total = 0f;
        var selectedLeft = -1f;
        var selectedRight = 0f;
        for (var i = 0; i < list.Count; i++)
        {
            var conversation = list[i];
            widths[i] = TabWidth(conversation, CacheOf(conversation), FriendOf(conversation), iconsOnly);
            if (ReferenceEquals(conversation, selected))
            {
                selectedLeft = total;
                selectedRight = total + widths[i];
            }
            total += widths[i] + gap;
        }
        total = MathF.Max(0f, total - gap);

        // The tabs get what the buttons at the right end leave. Arrows join
        // those when the tabs do not all fit.
        var room = width - button - (6f * scale);
        var overflow = total > room;
        if (overflow) room -= (button + gap) * 2f;
        room = MathF.Max(room, button);

        ref var offset = ref tabScroll[(int)group];
        var furthest = MathF.Max(0f, total - room);
        if (selected != null && selectedLeft >= 0f && !string.Equals(tabShown, selected.Key, StringComparison.Ordinal))
        {
            tabShown = selected.Key;
            if (selectedLeft < offset) offset = selectedLeft;
            else if (selectedRight > offset + room) offset = selectedRight - room;
        }

        var stripMax = new Vector2(start.X + room, start.Y + height);
        if (overflow && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(start, stripMax))
        {
            // Either wheel scrolls the tabs sideways: down or right for the ones further along.
            var io = ImGui.GetIO();
            var wheel = io.MouseWheelH != 0f ? io.MouseWheelH : io.MouseWheel;
            if (wheel != 0f) offset -= wheel * TabWheelStep * scale;
        }
        offset = Math.Clamp(offset, 0f, furthest);

        ImGui.PushClipRect(start, stripMax, true);
        try
        {
            var x = start.X - offset;
            for (var i = 0; i < list.Count; i++)
            {
                if (x + widths[i] > start.X && x < stripMax.X)
                    DrawConversationTab(drawList, list[i], new Vector2(x, start.Y), new Vector2(widths[i], height), iconsOnly);
                x += widths[i] + gap;
            }
        }
        finally
        {
            ImGui.PopClipRect();
        }

        var buttonY = start.Y + ((height - button) * 0.5f);
        var buttonX = start.X + width - button - (2f * scale);
        ImGui.SetCursorScreenPos(new Vector2(buttonX, buttonY));
        if (Painter.IconButton("##tabicons", iconsOnly ? FontAwesomeIcon.Font : FontAwesomeIcon.UserCircle,
                iconsOnly ? "Show names on the tabs" : "Show pictures only, to fit more tabs in", palette, button))
        {
            config.TabIconsOnly = !iconsOnly;
            plugin.SaveConfig();
        }

        if (overflow)
        {
            buttonX -= button + gap + (4f * scale);
            ImGui.SetCursorScreenPos(new Vector2(buttonX, buttonY));
            if (Painter.IconButton("##tabsright", FontAwesomeIcon.AngleRight, offset < furthest ? "More tabs this way" : string.Empty, palette, button, enabled: offset < furthest))
                offset = MathF.Min(furthest, offset + (room * 0.6f));

            buttonX -= button + gap;
            ImGui.SetCursorScreenPos(new Vector2(buttonX, buttonY));
            if (Painter.IconButton("##tabsleft", FontAwesomeIcon.AngleLeft, offset > 0f ? "More tabs this way" : string.Empty, palette, button, enabled: offset > 0f))
                offset = MathF.Max(0f, offset - (room * 0.6f));
        }

        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + height + gap));
    }

    private RowCache CacheOf(Conversation conversation)
    {
        if (conversation.ViewCache is not RowCache cache) conversation.ViewCache = cache = new RowCache();
        return cache;
    }

    private FriendStatus FriendOf(Conversation conversation) =>
        conversation.IsTell && config.ShowFriendStatus ? plugin.FriendStatus(conversation.Title, conversation.WorldId) : FriendStatus.None;

    /// <summary>How wide a tab is: its picture alone, or its name with a friend's dot, a linkshell's number and the unread count.</summary>
    private float TabWidth(Conversation conversation, RowCache cache, FriendStatus friend, bool iconsOnly)
    {
        var lineHeight = ImGui.GetTextLineHeight();
        if (iconsOnly) return (TabPictureRadius(lineHeight) * 2f) + (12f * scale);

        cache.Tab.Get(conversation.Title, MaxTabText * scale, layoutStamp, out var textWidth);
        var tabWidth = textWidth + (22f * scale);
        if (friend.IsFriend) tabWidth += PresenceDot(lineHeight) * 2f + (6f * scale);
        if (TabSlot(conversation) is { } slot) tabWidth += ImGui.CalcTextSize(slot).X + (6f * scale);
        if (conversation.Unread > 0) tabWidth += Painter.BadgeWidth(conversation.Unread, scale) + (6f * scale);
        return tabWidth;
    }

    /// <summary>A linkshell's number on its tab, as the game counts them. Null for anything else.</summary>
    private static string? TabSlot(Conversation conversation) =>
        conversation.Group is ChannelGroup.Linkshell or ChannelGroup.CrossWorld && conversation.Slot is > 0 and < 9 ? SlotLabels[conversation.Slot] : null;

    private float PresenceDot(float lineHeight) => MathF.Max(3f * scale, lineHeight * 0.2f);

    /// <summary>A picture on a tab: big enough for two initials, which tell people apart better than one.</summary>
    private static float TabPictureRadius(float lineHeight) => lineHeight * 0.82f;

    private void DrawConversationTab(ImDrawListPtr drawList, Conversation conversation, Vector2 min, Vector2 size, bool iconsOnly)
    {
        var cache = CacheOf(conversation);
        var friend = FriendOf(conversation);
        var max = min + size;

        ImGui.PushID(conversation.Key);
        try
        {
            ImGui.SetCursorScreenPos(min);
            if (ImGui.InvisibleButton("##tab", size)) Select(conversation, focus: true);
            var hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) ImGui.OpenPopup("##rowmenu");

            var isSelected = ReferenceEquals(conversation, selected);
            if (hovered && !isSelected) drawList.AddRectFilled(min, max, Painter.U32(palette.RowHover), 4f * scale);

            // Faded: muted, left, or a friend who is offline.
            var faded = conversation.Muted || (!conversation.IsTell && conversation.Slot == 0) || (friend.IsFriend && !friend.Online);
            var lineHeight = ImGui.GetTextLineHeight();
            var middle = min.Y + (size.Y * 0.5f);

            if (iconsOnly)
            {
                var radius = TabPictureRadius(lineHeight);
                var centre = new Vector2(min.X + (size.X * 0.5f), middle);
                DrawAvatar(drawList, conversation, cache, centre, radius, friend);
                if (friend.IsFriend) DrawPresence(drawList, centre, radius, friend, palette.WindowBg);
                if (conversation.Unread > 0) DrawUnreadDot(drawList, centre, radius, palette.WindowBg);
            }
            else
            {
                var x = min.X + (11f * scale);
                var textY = middle - (lineHeight * 0.5f);
                if (friend.IsFriend)
                {
                    var dot = PresenceDot(lineHeight);
                    DrawPresenceDot(drawList, new Vector2(x + dot, middle), dot, friend);
                    x += (dot * 2f) + (6f * scale);
                }

                if (TabSlot(conversation) is { } slot)
                {
                    drawList.AddText(new Vector2(x, textY), Painter.U32(palette.TextMuted), slot);
                    x += ImGui.CalcTextSize(slot).X + (6f * scale);
                }

                var title = cache.Tab.Get(conversation.Title, MaxTabText * scale, layoutStamp, out var titleWidth);
                var bright = !faded && (isSelected || conversation.Unread > 0);
                drawList.AddText(new Vector2(x, textY), Painter.U32(bright ? palette.Text : palette.TextMuted), title);

                if (conversation.Unread > 0)
                    Painter.Badge(drawList, new Vector2(x + titleWidth + (6f * scale) + Painter.BadgeWidth(conversation.Unread, scale), middle), conversation.Unread, palette, scale);
            }

            if (isSelected)
            {
                // Underlined in the conversation's own colour, as the kinds' tabs are in theirs.
                drawList.AddRectFilled(
                    new Vector2(min.X + (5f * scale), max.Y - (2f * scale)), new Vector2(max.X - (5f * scale), max.Y),
                    Painter.U32(ColourMath.LegibleOn(theme.ColourFor(conversation), palette.WindowBg, palette.Text)), scale);
            }

            if (hovered) DrawBanner(conversation, friend);
            DrawRowMenu(conversation);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>A small dot in a friend's status colour, as on their picture, for a tab that shows their name.</summary>
    private void DrawPresenceDot(ImDrawListPtr drawList, Vector2 centre, float radius, FriendStatus friend)
    {
        if (!friend.Online)
        {
            drawList.AddCircle(centre, radius - (0.5f * scale), Painter.U32(palette.TextMuted), 12, 1.5f * scale);
            return;
        }

        drawList.AddCircleFilled(centre, radius, Painter.U32(PresenceColour(friend)), 12);
        if (friend.Busy)
        {
            var halfWidth = radius * 0.68f;
            var halfHeight = MathF.Max(1f * scale, radius * 0.2f);
            drawList.AddRectFilled(new Vector2(centre.X - halfWidth, centre.Y - halfHeight), new Vector2(centre.X + halfWidth, centre.Y + halfHeight),
                Painter.U32(DoNotDisturbBar), halfHeight);
        }
    }

    /// <summary>The dot on a picture that says there is something unread.</summary>
    private void DrawUnreadDot(ImDrawListPtr drawList, Vector2 centre, float radius, Vector4 background)
    {
        var dot = new Vector2(centre.X + (radius * 0.72f), centre.Y - (radius * 0.72f));
        drawList.AddCircleFilled(dot, 5f * scale, Painter.U32(background with { W = 1f }));
        drawList.AddCircleFilled(dot, 3.6f * scale, Painter.U32(palette.Badge));
    }

    /// <summary>
    /// What was the banner above a conversation, now shown while its tab or
    /// row is pointed at: its picture and name, and under them the world and,
    /// for a friend, where they are and what they are doing, or the slot a
    /// linkshell is in. Pinning, muting and the rest are on a right-click.
    /// </summary>
    private void DrawBanner(Conversation conversation, FriendStatus friend)
    {
        // Lists and tabs narrow the spacing; a tooltip opened from inside them
        // would inherit that. In the banner's own colour, as it was over the conversation.
        var card = ColourMath.Mix(palette.WindowBg with { W = 1f }, palette.HeaderBg with { W = 1f }, palette.HeaderBg.W);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, normalPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, normalSpacing);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 6f * scale);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, card);
        ImGui.BeginTooltip();
        try
        {
            var lineHeight = ImGui.GetTextLineHeight();
            var radius = lineHeight * 0.95f;
            var origin = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new Vector2(radius * 2f, radius * 2f));
            ImGui.SameLine(0f, 10f * scale);

            ImGui.BeginGroup();
            ImGui.TextUnformatted(conversation.Title);
            ImGui.PushStyleColor(ImGuiCol.Text, ColourMath.LegibleOn(theme.ColourFor(conversation), card, palette.Text));
            ImGui.TextUnformatted(Subtitle(conversation));
            ImGui.PopStyleColor();

            var notes = BannerNotes(conversation);
            if (notes.Length > 0) ImGui.TextUnformatted(notes);
            ImGui.TextDisabled("Right-click for more");
            ImGui.EndGroup();

            var drawList = ImGui.GetWindowDrawList();
            var centre = origin + new Vector2(radius, radius);
            DrawAvatar(drawList, conversation, CacheOf(conversation), centre, radius, friend);
            if (friend.IsFriend) DrawPresence(drawList, centre, radius, friend, card);
        }
        finally
        {
            ImGui.EndTooltip();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(3);
        }
    }

    /// <summary>"3 unread · Pinned · Muted", or as much of it as applies.</summary>
    private static string BannerNotes(Conversation conversation)
    {
        var unread = conversation.Unread > 0 ? $"{conversation.Unread} unread" : null;
        var pinned = conversation.Pinned ? "Pinned" : null;
        var muted = conversation.Muted ? "Muted" : null;
        return string.Join(" · ", new[] { unread, pinned, muted }.Where(part => part != null));
    }
}
