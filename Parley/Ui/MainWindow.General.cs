using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Parley.Core;
using Parley.Core.Settings;
using Parley.Core.Theme;

namespace Parley.Ui;

/// <summary>
/// General: the game's own chat log, tab for tab as the player has set up the
/// game's chat tabs, in the game's Log Text Colors, with a box that types into
/// whichever channel the game's chat box is on, commands and all. The lines
/// are kept for the session only. Main window only, and only with the setting on.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>Stands in for a conversation's key wherever General's box needs one: keyboard focus and the caret.</summary>
    private const string GeneralKey = "\u0001general";

    /// <summary>The longest line the game's chat box takes.</summary>
    private const int GeneralMaxBytes = OutgoingQueue.MaxLineBytes;

    private const string ChannelMenu = "##channels";

    private bool generalShown;
    private string generalDraft = string.Empty;

    /// <summary>Set to show the newest line next frame: on coming to General, changing tab, or sending.</summary>
    private bool generalToEnd = true;

    private string generalNotice = string.Empty;
    private long generalNoticeUntil;

    /// <summary>Whether General can be shown in this window at all.</summary>
    private bool GeneralAvailable => only == null && config.GeneralChat && store.HasCharacter;

    /// <summary>Whether the window is on General this frame.</summary>
    private bool ShowingGeneral => generalShown && GeneralAvailable;

    /// <summary>
    /// Brings the window up on General: for Enter while Parley stands in for
    /// the game's chat, and for whatever would have typed in the game's chat
    /// box. Without focus it only makes sure General is on screen, as at login.
    /// </summary>
    /// <param name="prefill">Put at the start of General's box, as "/tell Name@World " when the game was asked to start a tell.</param>
    public void TypeInGeneral(bool focus = true, string prefill = "")
    {
        if (only != null) return;
        if (!GeneralAvailable)
        {
            if (!focus) return;
            Show();
            StartTyping();
            return;
        }

        if (searching) CloseSearch();
        ShowGeneral(true);
        if (!IsOpen) openQuietly = !focus;
        IsOpen = true;
        if (!focus) return;

        if (prefill.Length > 0 && !generalDraft.StartsWith(prefill, StringComparison.Ordinal))
        {
            generalDraft = prefill + generalDraft;
            caretTarget = -1;
        }

        Summon();
        BringToFront();
        focusComposer = GeneralKey;
    }

    private void ShowGeneral(bool show)
    {
        if (generalShown == show) return;
        generalShown = show;
        generalToEnd = true;
        selection.Clear();
        selecting = false;

        if (only == null && config.GeneralShown != show)
        {
            config.GeneralShown = show;
            plugin.SaveConfig();
        }
    }

    /// <summary>The game's tabs that are in use, which are the ones General has a tab for.</summary>
    private List<GameChatTab> GeneralTabs()
    {
        var tabs = new List<GameChatTab>(ChatLogFilter.TabCount);
        foreach (var tab in plugin.General.Tabs)
        {
            if (tab.InUse) tabs.Add(tab);
        }
        return tabs;
    }

    /// <summary>Alt+R on General steps through its tabs.</summary>
    private void CycleGeneralTab(int step)
    {
        var tabs = GeneralTabs();
        if (tabs.Count < 2) return;

        var at = tabs.FindIndex(tab => tab.Index == config.GeneralTab);
        var next = tabs[at < 0 ? 0 : (at + step + tabs.Count) % tabs.Count];
        SwitchGeneralTab(next.Index);
    }

    private void SwitchGeneralTab(int index)
    {
        if (config.GeneralTab == index) return;
        config.GeneralTab = index;
        plugin.SaveConfig();
        generalToEnd = true;
        selection.Clear();
        selecting = false;
    }

    private void DrawGeneral(float height)
    {
        var tabs = GeneralTabs();
        FollowAddedTab(tabs);

        // A tab that has gone out of use in the game gives way to the first one still in use.
        if (tabs.Count > 0 && tabs.FindIndex(tab => tab.Index == config.GeneralTab) < 0) SwitchGeneralTab(tabs[0].Index);

        var top = ImGui.GetCursorPosY();
        DrawGeneralTabs(tabs);

        var lines = tabs.Count == 0 ? plugin.General.All : plugin.General.Shown(config.GeneralTab);
        var style = ImGui.GetStyle();
        var inputHeight = ImGui.GetFrameHeight() + (style.ItemSpacing.Y * 2f);
        if (generalNotice.Length > 0 && now < generalNoticeUntil) inputHeight += ImGui.GetTextLineHeight() + style.ItemSpacing.Y;

        var listHeight = MathF.Max(40f * scale, height - (ImGui.GetCursorPosY() - top) - inputHeight);
        DrawGeneralLines(lines, listHeight);
        DrawGeneralInput();

        DrawLinkMenu();
        DrawItemMenu(null);
    }

    /// <summary>
    /// A strip of the game's tabs, by the names the player gave them in the
    /// game, then as in the game's own chat log a "+" for another tab while
    /// there is room for one, and a cog for the game's log settings.
    /// </summary>
    private void DrawGeneralTabs(List<GameChatTab> tabs)
    {
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var height = ImGui.GetTextLineHeight() + (8f * scale);
        var x = start.X + (4f * scale);

        foreach (var tab in tabs)
        {
            var label = tab.Name.Length > 0 ? tab.Name : $"Tab {tab.Index + 1}";
            var size = ImGui.CalcTextSize(label);
            var tabWidth = size.X + (24f * scale);

            ImGui.SetCursorScreenPos(new Vector2(x, start.Y));
            if (ImGui.InvisibleButton($"##gametab{tab.Index}", new Vector2(tabWidth, height))) SwitchGeneralTab(tab.Index);
            var hovered = ImGui.IsItemHovered();
            var active = tab.Index == config.GeneralTab;

            var min = new Vector2(x, start.Y);
            var max = new Vector2(x + tabWidth, start.Y + height);
            if (hovered && !active) drawList.AddRectFilled(min, max, Painter.U32(palette.RowHover), 4f * scale);
            drawList.AddText(new Vector2(x + (12f * scale), start.Y + ((height - size.Y) * 0.5f)), Painter.U32(active ? palette.Text : palette.TextMuted), label);
            if (active)
                drawList.AddRectFilled(new Vector2(min.X + (6f * scale), max.Y - (2f * scale)), new Vector2(max.X - (6f * scale), max.Y), Painter.U32(palette.Accent), scale);

            x += tabWidth + (2f * scale);
        }

        var button = height - (6f * scale);
        var buttonY = start.Y + ((height - button) * 0.5f);
        if (tabs.Count < ChatLogFilter.TabCount)
        {
            ImGui.SetCursorScreenPos(new Vector2(x + (2f * scale), buttonY));
            if (Painter.IconButton("##addgametab", FontAwesomeIcon.Plus, "Add a tab to the game's chat log\nThe game asks what to call it, and it appears here too.", palette, button))
                AddGeneralTab(tabs);
            x += button + (8f * scale);
        }

        ImGui.SetCursorScreenPos(new Vector2(x + (2f * scale), buttonY));
        if (Painter.IconButton("##logsettings", FontAwesomeIcon.Cog, "The game's Log Window Settings:\nwhat each tab shows, its colours and more", palette, button)
            && !plugin.OpenGameLogSettings())
            ShowGeneralNotice("The game would not open its Log Window Settings just now.");

        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + height + (2f * scale)));
    }

    /// <summary>The game's tabs in use when "+" was pressed, so the one it adds can be shown when it appears. Zero when none is on its way.</summary>
    private int tabsBeforeAdding;
    private long addingTabUntil;

    /// <summary>How long after "+" a new tab is still taken to be the one asked for: the game asks for its name first.</summary>
    private const long AddingTabMs = 120_000;

    private void AddGeneralTab(List<GameChatTab> tabs)
    {
        if (!plugin.AddGameChatTab())
        {
            ShowGeneralNotice("The game's chat log could not be reached to add a tab. Its own \"+\" does the same.");
            return;
        }

        tabsBeforeAdding = 0;
        foreach (var tab in tabs) tabsBeforeAdding |= 1 << tab.Index;
        addingTabUntil = now + AddingTabMs;
    }

    /// <summary>Goes to the tab the game has just added after "+", once it is there.</summary>
    private void FollowAddedTab(List<GameChatTab> tabs)
    {
        if (addingTabUntil == 0) return;
        if (now > addingTabUntil)
        {
            addingTabUntil = 0;
            return;
        }

        foreach (var tab in tabs)
        {
            if ((tabsBeforeAdding & (1 << tab.Index)) != 0) continue;
            addingTabUntil = 0;
            SwitchGeneralTab(tab.Index);
            return;
        }
    }

    private void ShowGeneralNotice(string text)
    {
        generalNotice = text;
        generalNoticeUntil = now + NoticeMs;
    }

    /// <summary>
    /// The lines, oldest at the top, each with its time and in its colour. It
    /// follows the newest line while scrolled to the bottom, as the game's log
    /// does, and stays put once scrolled up to read back.
    /// </summary>
    private void DrawGeneralLines(IReadOnlyList<ChatMessage> lines, float height)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var visible = ImGui.BeginChild("##generallog", new Vector2(0f, height), false);
        ImGui.PopStyleVar();

        try
        {
            if (!visible) return;

            BeginText(lines);
            var width = ImGui.GetContentRegionAvail().X;
            if (lines.Count == 0)
            {
                DrawCentred("Chat from now on shows up here, the way it does in this tab of the game's chat log.", new Vector2(width, height));
                return;
            }

            // Whether the list was at the bottom before this frame added anything to it.
            var following = !selecting && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1f;

            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos() + new Vector2(6f * scale, 4f * scale);
            var viewTop = ImGui.GetWindowPos().Y;
            var viewHeight = ImGui.GetWindowHeight();
            var lineHeight = ImGui.GetTextLineHeight();
            var gap = 2f * scale;
            var showTime = look.Timestamps != TimestampStyle.None;
            var clockWidth = showTime ? ImGui.CalcTextSize(config.Use24Hour ? "[00:00] " : "[00:00 PM] ").X : 0f;
            var wrap = MathF.Max(40f * scale, width - clockWidth - (12f * scale));
            var clockColour = Painter.U32(palette.TextMuted);

            var y = 0f;
            for (var i = 0; i < lines.Count; i++)
            {
                var message = lines[i];
                var rowHeight = MathF.Max(lineHeight, MeasureText(message, wrap).Y) + gap;
                var rowTop = origin.Y + y;
                if (rowTop + rowHeight >= viewTop && rowTop <= viewTop + viewHeight)
                {
                    if (showTime) drawList.AddText(new Vector2(origin.X, rowTop), clockColour, "[" + Clock(message) + "]");
                    DrawMessageText(drawList, message, i, new Vector2(origin.X + clockWidth, rowTop), wrap, GeneralColour(message), palette.WindowBg);
                }
                y += rowHeight;
            }

            ImGui.SetCursorScreenPos(ImGui.GetWindowPos() - new Vector2(0f, ImGui.GetScrollY()));
            ImGui.Dummy(new Vector2(width, MathF.Max(y + (8f * scale), viewHeight)));
            if (generalToEnd || following)
            {
                ImGui.SetScrollHereY(1f);
                generalToEnd = false;
            }

            HandleTextMouse(ImGui.GetWindowPos(), width, viewHeight, (notches, step) => ImGui.SetScrollY(ImGui.GetScrollY() - (notches * step)));
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    /// <summary>A line's colour from the game's Log Text Colors, kept readable on Parley's background.</summary>
    private Vector4 GeneralColour(ChatMessage message)
    {
        var colour = plugin.ChatColour(ChatLogFilter.KindOf(message.LogInfo)) ?? palette.Text;
        return ColourMath.LegibleOn(colour, palette.WindowBg, palette.Text);
    }

    /// <summary>
    /// The box at the bottom: the channel the game's chat box is on, in its
    /// colour (click it to change channel), then what to say. Whatever is
    /// typed goes to the game as though typed in its own chat box.
    /// </summary>
    private void DrawGeneralInput()
    {
        var (channel, label, tellTo) = plugin.CurrentChannel();
        var name = channel != null ? ChannelName(channel)
            : tellTo.Length > 0 ? $"Tell {tellTo}"
            : label.TrimStart('/');
        var kind = channel?.LogKind ?? (tellTo.Length > 0 ? ChatChannels.TellLogKind : 10);
        var channelColour = ColourMath.LegibleOn(plugin.ChatColour(kind) ?? palette.Text, palette.WindowBg, palette.Text);

        var canSend = store.HasCharacter;
        canReply = canSend;

        var style = ImGui.GetStyle();
        ImGui.Dummy(new Vector2(1f, style.ItemSpacing.Y * 0.5f));

        ImGui.PushStyleColor(ImGuiCol.Text, channelColour);
        var picked = ImGui.Button(name.Length > 0 ? $"{name}##channel" : "Channel##channel");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("The channel the game's chat box is on.\nClick to switch, or type a command such as /p as in the game.");
        if (picked) ImGui.OpenPopup(ChannelMenu);
        DrawChannelMenu();

        ImGui.SameLine();
        var buttonSize = ImGui.GetFrameHeight();
        var inputWidth = MathF.Max(40f * scale, ImGui.GetContentRegionAvail().X - (buttonSize * 3f) - (style.ItemSpacing.X * 3f));

        if (focusComposer is WhicheverIsShown or GeneralKey)
        {
            focusComposer = null;
            if (canSend)
            {
                ImGui.SetKeyboardFocusHere();
                caretFrames = CaretFrames;
            }
        }

        var draft = generalDraft;
        ImGui.SetNextItemWidth(inputWidth);
        ImGui.BeginDisabled(!canSend);
        caretDrawing = GeneralKey;
        ImGui.InputTextWithHint("##general", canSend ? $"Say something in {name}, or type a command" : "Log in to chat.", ref draft, GeneralMaxBytes, ReplyBoxFlags, placeCaret);
        var entered = ImGui.IsItemDeactivated() && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter));
        ImGui.EndDisabled();
        if (caretFrames > 0) caretFrames--;

        // As in a conversation's box: Escape does not throw away what was typed.
        if (!ImGui.IsKeyPressed(ImGuiKey.Escape) && !string.Equals(draft, generalDraft, StringComparison.Ordinal))
        {
            generalDraft = draft;
            generalNotice = string.Empty;
        }

        ImGui.SameLine();
        DrawAutoTranslateButton(null, canSend, buttonSize);

        ImGui.SameLine();
        DrawSymbolButton(null, canSend, buttonSize);

        ImGui.SameLine();
        var ready = canSend && !string.IsNullOrWhiteSpace(generalDraft);
        var clicked = Painter.IconButton("##sendgeneral", FontAwesomeIcon.PaperPlane, ready ? "Send" : string.Empty, palette, buttonSize,
            ready ? ColourMath.LegibleOn(palette.Accent, palette.WindowBg, palette.Text) : null, ready);

        // Alt+Enter brings Parley up to type; pressed while already typing here it does nothing.
        if (entered && ImGui.GetIO().KeyAlt)
        {
            entered = false;
            focusComposer = GeneralKey;
        }

        if ((entered || clicked) && ready) SendGeneralLine(entered);
        else if (entered) AfterGeneralEnter();

        if (generalNotice.Length > 0 && now < generalNoticeUntil)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, palette.Error);
            ImGui.TextUnformatted(Painter.Fit(generalNotice, ImGui.GetContentRegionAvail().X));
            ImGui.PopStyleColor();
        }
    }

    private void SendGeneralLine(bool byEnter)
    {
        if (plugin.SendGeneral(generalDraft))
        {
            Sent(generalDraft);
            generalDraft = string.Empty;
            generalNotice = string.Empty;
            generalToEnd = true;
        }
        else
        {
            generalNotice = "The game would not take that line. It may be too long for one message.";
            generalNoticeUntil = now + NoticeMs;
        }

        if (byEnter) AfterGeneralEnter();
        else focusComposer = GeneralKey;
    }

    /// <summary>Enter in General's box ends typing, as in the game's chat box and a conversation's.</summary>
    private void AfterGeneralEnter()
    {
        if (config.ReleaseKeyboardOnEnter) releaseKeyboard = true;
        else focusComposer = GeneralKey;
    }

    /// <summary>The channels to switch to: the everyday ones, and the linkshells and free company the character is in.</summary>
    private void DrawChannelMenu()
    {
        if (!BeginMenu(ChannelMenu)) return;
        try
        {
            ImGui.TextDisabled("Switch the chat box to");
            ImGui.Separator();
            foreach (var channel in ChatChannels.All)
            {
                if (channel.Group is { } group && !InChannel(group, channel.Slot)) continue;

                var colour = ColourMath.LegibleOn(plugin.ChatColour(channel.LogKind) ?? palette.Text, palette.PopupBg, palette.Text);
                ImGui.PushStyleColor(ImGuiCol.Text, colour);
                var chosen = ImGui.MenuItem(ChannelName(channel));
                ImGui.PopStyleColor();
                if (chosen && !plugin.SendGeneral(channel.Command))
                {
                    generalNotice = $"The game would not switch to {ChannelName(channel)}.";
                    generalNoticeUntil = now + NoticeMs;
                }
            }
        }
        finally
        {
            EndMenu();
        }
    }

    /// <summary>A channel's name, with the linkshell's own name for a linkshell slot when Parley knows it.</summary>
    private string ChannelName(ChatChannel channel)
    {
        if (channel.Group is not { } group || group == ChannelGroup.FreeCompany) return channel.Name;

        foreach (var conversation in store.InGroup(group))
        {
            if (conversation.Slot == channel.Slot && !conversation.Closed && conversation.Title.Length > 0)
                return group == ChannelGroup.CrossWorld ? $"CWLS{channel.Slot}: {conversation.Title}" : $"LS{channel.Slot}: {conversation.Title}";
        }
        return channel.Name;
    }

    /// <summary>Whether the character is in the free company or linkshell slot a channel is for.</summary>
    private bool InChannel(ChannelGroup group, int slot)
    {
        foreach (var conversation in store.InGroup(group))
        {
            if (conversation.Slot == slot && !conversation.Closed) return true;
        }
        return false;
    }
}
