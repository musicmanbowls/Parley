using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core;
using Parley.Core.Settings;
using Parley.Core.Theme;

namespace Parley.Ui;

internal sealed partial class MainWindow
{
    private const float MinSidebarWidth = 120f;
    private const float MaxSidebarWidth = 480f;

    private static readonly string[] SlotLabels = ["?", "1", "2", "3", "4", "5", "6", "7", "8"];

    /// <summary>
    /// The text of one sidebar row, kept between frames. Each part is rebuilt
    /// only when what it was built from changes.
    /// </summary>
    private sealed class RowCache
    {
        public FitCache Title;
        public FitCache Preview;

        /// <summary>The name on the conversation's tab, when tabs are used instead of the list.</summary>
        public FitCache Tab;

        // The time beside the name.
        public long StampActivity = -1;
        public int StampDay;
        public int StampLayout;
        public string Stamp = string.Empty;
        public float StampWidth;

        // The line under the name, before fitting.
        public string? PreviewFrom;
        public string? PreviewSender;
        public string? PreviewDraft;
        public int PreviewSlot = -1;
        public string PreviewSource = string.Empty;

        // The avatar.
        public string? InitialsFrom;
        public string Initials = "?";

        // A colour picked for the person a tell is with, as of a name colour version.
        public int NameColourStamp = -1;
        public string? NameColourFrom;
        public bool HasNameColour;
        public Vector4 NameColour;
    }

    /// <summary>
    /// The list of conversations down the left: one row each, newest or
    /// lowest slot first, and under them the button that shrinks the list to
    /// pictures and back.
    /// </summary>
    private void DrawSidebar(float height)
    {
        var lineHeight = ImGui.GetTextLineHeight();
        var collapsed = ListIconsOnly;
        var padding = 4f * scale;

        var width = collapsed
            ? (lineHeight * 1.9f) + (12f * scale) + (padding * 2f)
            : Math.Clamp(config.SidebarWidth * scale, MinSidebarWidth * scale,
                MathF.Max(MinSidebarWidth * scale, ImGui.GetContentRegionAvail().X * 0.6f));

        ImGui.PushStyleColor(ImGuiCol.ChildBg, palette.SidebarBg);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, padding));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, 2f * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f * scale);
        var visible = ImGui.BeginChild("##sidebar", new Vector2(width, height), false,
            ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        try
        {
            if (visible)
            {
                var toggle = ImGui.GetFrameHeight();
                DrawSidebarRows(collapsed, MathF.Max(1f, ImGui.GetContentRegionAvail().Y - toggle - (4f * scale)));
                DrawListToggle(collapsed, toggle);
            }
        }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor();
        }
    }

    private void DrawSidebarRows(bool collapsed, float height)
    {
        // Shrunk to a column of avatars there is no room for a scrollbar beside
        // them. The wheel still scrolls the list.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        var visible = ImGui.BeginChild("##rows", new Vector2(0f, height), false, collapsed ? ImGuiWindowFlags.NoScrollbar : ImGuiWindowFlags.None);
        try
        {
            if (!visible) return;

            var list = store.InGroup(group);
            var rowWidth = ImGui.GetContentRegionAvail().X;
            if (list.Count == 0 && !collapsed)
            {
                DrawSidebarEmpty(rowWidth);
                return;
            }

            // A row's menu can close or delete a conversation. That only
            // marks the list for rebuilding on the next InGroup call, so
            // this copy is stable for the length of the loop.
            for (var i = 0; i < list.Count; i++) DrawRow(list[i], rowWidth, collapsed);
        }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
    }

    /// <summary>At the foot of the list: shrink it to pictures, or bring the names back. Double-clicking the divider does the same.</summary>
    private void DrawListToggle(bool collapsed, float size)
    {
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (2f * scale));
        var tip = listForcedNarrow ? "The window is too narrow for names, so the list shows pictures.\nWiden the window to see them, or turn this off under Appearance."
            : collapsed ? "Show the names" : "Shrink the list to pictures";
        if (Painter.IconButton("##collapse", collapsed ? FontAwesomeIcon.AngleDoubleRight : FontAwesomeIcon.AngleDoubleLeft, tip, palette, size, enabled: !listForcedNarrow))
        {
            config.SidebarCollapsed = !collapsed;
            plugin.SaveConfig();
        }
    }

    private void DrawSidebarEmpty(float width)
    {
        var text = group switch
        {
            ChannelGroup.Tell => store.IndexLoaded ? "No tells yet.\nUse + to start one." : "Loading…",
            ChannelGroup.Linkshell => "You are not in any linkshells.",
            ChannelGroup.FreeCompany => "You are not in a free company.",
            _ => "You are not in any cross-world linkshells.",
        };

        ImGui.Dummy(new Vector2(1f, 6f * scale));
        ImGui.PushStyleColor(ImGuiCol.Text, palette.TextMuted);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
    }

    private void DrawRow(Conversation conversation, float width, bool collapsed)
    {
        if (width <= 0f) return;

        var lineHeight = ImGui.GetTextLineHeight();
        var twoLine = !collapsed && config.SidebarPreviews;
        var radius = twoLine || collapsed ? lineHeight * 0.95f : lineHeight * 0.62f;
        var rowHeight = collapsed
            ? (radius * 2f) + (10f * scale)
            : twoLine ? (lineHeight * 2f) + (14f * scale) : lineHeight + (12f * scale);

        var min = ImGui.GetCursorScreenPos();
        var max = new Vector2(min.X + width, min.Y + rowHeight);
        var cache = conversation.ViewCache as RowCache;
        if (cache == null) conversation.ViewCache = cache = new RowCache();

        ImGui.PushID(conversation.Key);
        try
        {
            if (ImGui.InvisibleButton("##row", new Vector2(width, rowHeight))) Select(conversation, focus: true);
            var hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) ImGui.OpenPopup("##rowmenu");

            var drawList = ImGui.GetWindowDrawList();
            var isSelected = ReferenceEquals(conversation, selected);
            if (isSelected) drawList.AddRectFilled(min, max, Painter.U32(palette.RowSelected), 6f * scale);
            else if (hovered) drawList.AddRectFilled(min, max, Painter.U32(palette.RowHover), 6f * scale);

            var friend = FriendOf(conversation);

            var centre = new Vector2(min.X + (6f * scale) + radius, min.Y + (rowHeight * 0.5f));
            DrawAvatar(drawList, conversation, cache, centre, radius, friend);
            if (friend.IsFriend) DrawPresence(drawList, centre, radius, friend, palette.SidebarBg);

            if (collapsed)
            {
                // No room for a count, so an unread conversation gets a dot on its avatar.
                if (conversation.Unread > 0) DrawUnreadDot(drawList, centre, radius, palette.SidebarBg);
            }
            else
            {
                DrawRowText(drawList, conversation, cache, centre.X + radius + (8f * scale), max.X - (8f * scale), min.Y, rowHeight, twoLine, isSelected, friend);
            }

            // Who it is with, their world and whereabouts: the banner, while pointed at.
            if (hovered) DrawBanner(conversation, friend);
            DrawRowMenu(conversation);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private void DrawRowText(ImDrawListPtr drawList, Conversation conversation, RowCache cache, float left, float right, float top, float rowHeight, bool twoLine, bool isSelected, FriendStatus friend)
    {
        var lineHeight = ImGui.GetTextLineHeight();
        var blockHeight = twoLine ? (lineHeight * 2f) + (2f * scale) : lineHeight;
        var firstLine = top + ((rowHeight - blockHeight) * 0.5f);
        var titleRight = right;

        // Right-hand end of the first line: the time, or on a one-line row the badge.
        if (twoLine)
        {
            RefreshStamp(conversation, cache);
            if (cache.Stamp.Length > 0)
            {
                drawList.AddText(new Vector2(right - cache.StampWidth, firstLine), Painter.U32(palette.TextMuted), cache.Stamp);
                titleRight = right - cache.StampWidth - (8f * scale);
            }
        }
        else if (conversation.Unread > 0)
        {
            Painter.Badge(drawList, new Vector2(right, firstLine + (lineHeight * 0.5f)), conversation.Unread, palette, scale);
            titleRight = right - Painter.BadgeWidth(conversation.Unread, scale) - (6f * scale);
        }

        // A small marker after the name for pinned and muted conversations,
        // and one for someone on the friend list.
        var marker = conversation.Muted ? FontAwesomeIcon.BellSlash : conversation.Pinned ? FontAwesomeIcon.Thumbtack : FontAwesomeIcon.None;
        var markerWidth = marker != FontAwesomeIcon.None ? Painter.IconSize(marker).X + (6f * scale) : 0f;
        // The friend marker is drawn small; it only needs to be noticed.
        var friendIcon = Vector2.Zero;
        if (friend.IsFriend)
        {
            ImGui.SetWindowFontScale(0.75f);
            friendIcon = Painter.IconSize(FontAwesomeIcon.UserFriends);
            ImGui.SetWindowFontScale(1f);
        }
        var friendWidth = friend.IsFriend ? friendIcon.X + (6f * scale) : 0f;

        var title = cache.Title.Get(conversation.Title, titleRight - left - markerWidth - friendWidth, layoutStamp, out var titleWidth);
        var faded = conversation.Muted || (friend.IsFriend && !friend.Online);
        drawList.AddText(new Vector2(left, firstLine), Painter.U32(faded ? palette.TextMuted : palette.Text), title);

        var markerX = left + titleWidth + (6f * scale);
        var markerColour = ColourMath.WithAlpha(palette.TextMuted, palette.TextMuted.W * 0.8f);
        if (friend.IsFriend)
        {
            ImGui.SetWindowFontScale(0.75f);
            Painter.Icon(drawList, new Vector2(markerX, firstLine + ((ImGui.GetTextLineHeight() / 0.75f) - friendIcon.Y) * 0.5f), FontAwesomeIcon.UserFriends, markerColour);
            ImGui.SetWindowFontScale(1f);
            markerX += friendWidth;
        }
        if (marker != FontAwesomeIcon.None) Painter.Icon(drawList, new Vector2(markerX, firstLine), marker, markerColour);

        if (!twoLine) return;

        var secondLine = firstLine + lineHeight + (2f * scale);
        var previewRight = right;
        if (conversation.Unread > 0)
        {
            Painter.Badge(drawList, new Vector2(right, secondLine + (lineHeight * 0.5f)), conversation.Unread, palette, scale);
            previewRight = right - Painter.BadgeWidth(conversation.Unread, scale) - (6f * scale);
        }

        // A draft is shown in place of the last message, except on the row
        // whose reply box is on screen with the same text in it.
        var hasDraft = conversation.Draft.Length > 0 && !isSelected;
        var source = PreviewSource(conversation, cache, hasDraft);
        var preview = cache.Preview.Get(source, previewRight - left, layoutStamp, out _);
        var previewColour = hasDraft
            ? ColourMath.LegibleOn(palette.Accent, palette.SidebarBg, palette.Text)
            : conversation.Unread > 0 ? palette.Text : palette.TextMuted;
        drawList.AddText(new Vector2(left, secondLine), Painter.U32(previewColour), preview);
    }

    private void RefreshStamp(Conversation conversation, RowCache cache)
    {
        if (cache.StampActivity == conversation.LastActivity && cache.StampDay == today && cache.StampLayout == layoutStamp) return;

        cache.StampActivity = conversation.LastActivity;
        cache.StampDay = today;
        cache.StampLayout = layoutStamp;
        cache.Stamp = TimeText.Sidebar(conversation.LastActivity, now, config.Use24Hour);
        cache.StampWidth = cache.Stamp.Length > 0 ? ImGui.CalcTextSize(cache.Stamp).X : 0f;
    }

    /// <summary>The second line of a row: a draft, what was last said, or failing that something about the conversation.</summary>
    private static string PreviewSource(Conversation conversation, RowCache cache, bool hasDraft)
    {
        var draft = hasDraft ? conversation.Draft : null;
        if (ReferenceEquals(cache.PreviewFrom, conversation.LastPreview)
            && ReferenceEquals(cache.PreviewSender, conversation.LastSender)
            && ReferenceEquals(cache.PreviewDraft, draft)
            && cache.PreviewSlot == conversation.Slot)
            return cache.PreviewSource;

        cache.PreviewFrom = conversation.LastPreview;
        cache.PreviewSender = conversation.LastSender;
        cache.PreviewDraft = draft;
        cache.PreviewSlot = conversation.Slot;
        cache.PreviewSource = draft != null ? "Draft: " + draft : PreviewLine(conversation);
        return cache.PreviewSource;
    }

    private static string PreviewLine(Conversation conversation)
    {
        if (conversation.LastPreview.Length == 0)
        {
            if (conversation.IsTell) return conversation.WorldName;
            return conversation.Slot > 0 ? conversation.Group.SlotLabel(conversation.Slot) : "No longer a member";
        }

        // An empty sender means the local player spoke last.
        if (conversation.LastSender.Length == 0) return "You: " + conversation.LastPreview;
        if (conversation.IsTell) return conversation.LastPreview;

        var space = conversation.LastSender.IndexOf(' ');
        var firstName = space > 0 ? conversation.LastSender[..space] : conversation.LastSender;
        return $"{firstName}: {conversation.LastPreview}";
    }

    /// <summary>
    /// A dot on a friend's avatar: green online, amber away, red in a duty,
    /// a do-not-disturb sign (red, with a dark bar across it) for someone who
    /// has set themselves busy, and a grey ring when offline.
    /// </summary>
    /// <param name="background">What the avatar sits on, for the ring that sets the dot apart from it.</param>
    private void DrawPresence(ImDrawListPtr drawList, Vector2 centre, float radius, FriendStatus friend, Vector4 background)
    {
        var dot = new Vector2(centre.X + (radius * 0.72f), centre.Y + (radius * 0.72f));
        var size = MathF.Max(4f * scale, radius * 0.34f);
        drawList.AddCircleFilled(dot, size + (1.5f * scale), Painter.U32(background with { W = 1f }));

        if (!friend.Online)
        {
            drawList.AddCircle(dot, size - (0.5f * scale), Painter.U32(palette.TextMuted), 12, 1.5f * scale);
            return;
        }

        drawList.AddCircleFilled(dot, size, Painter.U32(PresenceColour(friend)), 16);

        if (friend.Busy)
        {
            // Do not disturb: a bar across the middle, rounded at the ends.
            var halfWidth = size * 0.68f;
            var halfHeight = MathF.Max(1f * scale, size * 0.2f);
            drawList.AddRectFilled(
                new Vector2(dot.X - halfWidth, dot.Y - halfHeight), new Vector2(dot.X + halfWidth, dot.Y + halfHeight),
                Painter.U32(DoNotDisturbBar), halfHeight);
        }
    }

    private static Vector4 PresenceColour(FriendStatus friend) =>
        friend.Busy || friend.InDuty ? PresenceRed : friend.Away ? PresenceAway : PresenceOnline;

    private static readonly Vector4 PresenceOnline = ColourMath.FromRgb(0x43B75F);
    private static readonly Vector4 PresenceAway = ColourMath.FromRgb(0xE3A33A);
    private static readonly Vector4 PresenceRed = ColourMath.FromRgb(0xEF4848);
    private static readonly Vector4 DoNotDisturbBar = ColourMath.FromRgb(0x323338);

    private void DrawAvatar(ImDrawListPtr drawList, Conversation conversation, RowCache cache, Vector2 centre, float radius, FriendStatus friend)
    {
        if (!ReferenceEquals(cache.InitialsFrom, conversation.Title))
        {
            cache.InitialsFrom = conversation.Title;
            cache.Initials = PlayerName.Initials(conversation.Title);
        }

        Vector4 fill;
        string label;
        if (conversation.IsTell)
        {
            if (cache.NameColourStamp != config.NameColoursVersion || !ReferenceEquals(cache.NameColourFrom, conversation.Title))
            {
                cache.NameColourStamp = config.NameColoursVersion;
                cache.NameColourFrom = conversation.Title;
                cache.HasNameColour = config.NameColours.Count > 0
                                      && config.NameColours.TryGetValue(Configuration.NameKey(conversation.Title, conversation.WorldName), out var hex)
                                      && ColourMath.TryParseHex(hex, out cache.NameColour);
            }

            fill = cache.HasNameColour
                ? cache.NameColour with { W = 1f }
                : AutomaticNameColour(conversation, conversation.Title, outgoing: false);
            label = cache.Initials;
        }
        else
        {
            // Linkshells go by their slot number, the way the game refers to
            // them. A free company has no number to go by.
            fill = theme.ColourFor(conversation);
            label = conversation.Group != ChannelGroup.FreeCompany && conversation.Slot is > 0 and < 9
                ? SlotLabels[conversation.Slot]
                : cache.Initials;
        }

        if (conversation.Muted || (!conversation.IsTell && conversation.Slot == 0) || (friend.IsFriend && !friend.Online))
            fill = ColourMath.Mix(fill, palette.SidebarBg with { W = 1f }, 0.55f);

        // One glyph reads better than two on the small disc of a one-line row.
        if (radius < ImGui.GetTextLineHeight() * 0.8f && label.Length > 1)
            Painter.Avatar(drawList, centre, radius, SingleGlyph(label), fill);
        else
            Painter.Avatar(drawList, centre, radius, label, fill);
    }

    /// <summary>The first letter of a label as a string, without making a new one for the common letters.</summary>
    private static string SingleGlyph(string label)
    {
        var first = label[0];
        return first is >= 'A' and <= 'Z' ? Letters[first - 'A'] : first.ToString();
    }

    private static readonly string[] Letters =
    [
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
    ];

    private void DrawRowMenu(Conversation conversation)
    {
        if (!BeginMenu("##rowmenu")) return;
        try
        {
            DrawConversationMenuItems(conversation);
        }
        finally
        {
            EndMenu();
        }
    }

    /// <summary>What can be done to a conversation, shared by its sidebar row and the header's menu.</summary>
    private void DrawConversationMenuItems(Conversation conversation)
    {
        ImGui.TextDisabled(conversation.DisplayName);
        ImGui.Separator();

        if (ImGui.MenuItem("Mark as read", false, conversation.Unread > 0))
        {
            store.MarkRead(conversation);
            store.ClearUnreadMarker(conversation);
        }

        if (ImGui.MenuItem(conversation.Pinned ? "Unpin" : "Pin to top")) store.SetPinned(conversation, !conversation.Pinned);
        if (ImGui.MenuItem(conversation.Muted ? "Unmute" : "Mute")) store.SetMuted(conversation, !conversation.Muted);
        if (ImGui.MenuItem("Copy name")) ImGui.SetClipboardText(conversation.DisplayName);
        if (conversation.IsTell && ImGui.MenuItem("Name colour…"))
            AskForNameColour(conversation, conversation.Title, conversation.WorldName, outgoing: false);

        ImGui.Separator();
        if (ImGui.MenuItem("Close")) store.Close(conversation);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hides this conversation and keeps its history.\nIt comes back when there is a new message, unless it is muted.");

        if (ImGui.MenuItem("Clear history…", false, store.SavesHistory || conversation.Messages.Count > 0))
            AskToConfirm(ConfirmKind.ClearHistory, conversation);

        // A linkshell the character is still in would only reappear at once.
        if (conversation.IsTell || conversation.Slot == 0)
        {
            if (ImGui.MenuItem("Delete conversation…")) AskToConfirm(ConfirmKind.Forget, conversation);
        }
    }

    /// <summary>The thin strip between sidebar and conversation: drag to resize, double-click to collapse.</summary>
    private void DrawSplitter(float height)
    {
        var width = 6f * scale;
        if (height <= 0f) return;

        ImGui.InvisibleButton("##splitter", new Vector2(width, height));
        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();

        if (hovered || active) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        if (hovered && !listForcedNarrow && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            config.SidebarCollapsed = !config.SidebarCollapsed;
            plugin.SaveConfig();
        }
        else if (active && !ListIconsOnly)
        {
            var delta = ImGui.GetIO().MouseDelta.X;
            if (delta != 0f)
            {
                config.SidebarWidth = Math.Clamp(config.SidebarWidth + (delta / scale), MinSidebarWidth, MaxSidebarWidth);
                plugin.SaveConfig();
            }
        }

        if (hovered || active)
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var middle = (min.X + max.X) * 0.5f;
            ImGui.GetWindowDrawList().AddLine(
                new Vector2(middle, min.Y + (4f * scale)), new Vector2(middle, max.Y - (4f * scale)),
                Painter.U32(ColourMath.WithAlpha(palette.Accent, active ? 0.9f : 0.5f)), 2f * scale);
        }
    }
}
