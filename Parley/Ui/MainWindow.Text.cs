using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.ImGuiSeStringRenderer;
using Dalamud.Interface.Utility;
using Parley.Core;
using Parley.Core.Text;
using Parley.Core.Theme;
using Parley.Game;

namespace Parley.Ui;

/// <summary>
/// Message text: laying it out, drawing it with its colours and links, and
/// everything the mouse does to it. Clicking a link, dragging to select,
/// double-clicking a word and copying with Ctrl+C all work from the positions
/// worked out here, so what is highlighted is exactly what is drawn.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>One message's text as drawn this frame, top to bottom down the list.</summary>
    private readonly record struct Placement(ChatMessage Message, int Index, Vector2 Origin, TextLayout Layout, Vector4 Background)
    {
        public float Top => Origin.Y;
        public float Bottom => Origin.Y + Layout.Height;
    }

    /// <summary>A sender's name drawn over a message in a group chat, which opens a tell when clicked.</summary>
    private readonly record struct NameSpot(Vector2 Min, Vector2 Max, string Name, ushort World, Vector4 Colour);

    /// <summary>What is under the cursor: a link in a message, a name, or nothing that does anything.</summary>
    private readonly record struct Target(int Placement, TextLink? Link, int Name)
    {
        public static readonly Target None = new(-1, null, -1);
        public bool IsNone => Link == null && Name < 0;
        public bool Same(Target other) => ReferenceEquals(Link, other.Link) && Name == other.Name;
    }

    private readonly List<Placement> placements = [];
    private readonly List<NameSpot> nameSpots = [];
    private readonly List<(Vector2 Min, Vector2 Max)> scratchRects = [];
    private readonly TextSelection selection = new();
    private readonly Dictionary<long, Vector4> keyColours = [];
    private readonly Dictionary<byte[], Vector2> objectSizes = new(ReferenceEqualityComparer.Instance);

    private TextSelection.Range selectionRange;
    private bool hasSelectionRange;
    private bool selecting;
    private Target pressTarget = Target.None;
    private Vector2 pressAt;
    private TextLink? menuLink;
    private bool rightClickTaken;
    private bool itemTooltipSeen;

    // ------------------------------------------------------------------
    // Building and measuring
    // ------------------------------------------------------------------

    private static RichText Parsed(ChatMessage message) => message.Parsed ??= SeStringText.For(message);

    /// <summary>The message laid out at this width, from the cache on the message when nothing has changed.</summary>
    private TextLayout LayoutOf(ChatMessage message, float wrap)
    {
        if (message.Layout != null && message.LayoutStamp == layoutStamp && message.LayoutWrap == wrap) return message.Layout;

        var font = ImGui.GetFont();
        var size = ImGui.GetFontSize();
        var factor = size / font.FontSize;
        var kerning = font.KerningPairs.Size > 0;
        var layout = TextLayout.Build(Parsed(message), wrap, ImGui.GetTextLineHeight(),
            (ch, previous) =>
            {
                var advance = font.GetCharAdvance(ch);
                if (kerning && previous != '\0') advance += font.GetDistanceAdjustmentForPair(previous, ch);
                return advance * factor;
            },
            index => ObjectSize(Parsed(message).Objects[index]));

        message.Layout = layout;
        message.LayoutStamp = layoutStamp;
        message.LayoutWrap = wrap;
        message.TextSize = new Vector2(layout.Width, layout.Height);
        return layout;
    }

    private Vector2 MeasureText(ChatMessage message, float wrap)
    {
        var layout = LayoutOf(message, wrap);
        return new Vector2(layout.Width, layout.Height);
    }

    /// <summary>How big an inline icon is, asked of the game's own text renderer once and remembered.</summary>
    private Vector2 ObjectSize(byte[] encoded)
    {
        if (objectSizes.TryGetValue(encoded, out var size)) return size;

        var line = ImGui.GetTextLineHeight();
        size = new Vector2(line, line);
        try
        {
            var measured = ImGuiHelpers.SeStringWrapped(encoded, new SeStringDrawParams
            {
                TargetDrawList = default(ImDrawListPtr),
                Font = ImGui.GetFont(),
                FontSize = ImGui.GetFontSize(),
                WrapWidth = float.MaxValue,
            }).Size;
            if (measured.X > 0f && measured.Y > 0f) size = measured;
        }
        catch (Exception ex)
        {
            ReportRichFailure(ex);
        }

        objectSizes[encoded] = size;
        return size;
    }

    // ------------------------------------------------------------------
    // Drawing
    // ------------------------------------------------------------------

    /// <summary>Starts a frame of the message list: forgets last frame's positions and works out the selection afresh.</summary>
    private void BeginText(Conversation conversation)
    {
        placements.Clear();
        nameSpots.Clear();
        rightClickTaken = false;
        hasSelectionRange = selection.Resolve(conversation.Messages, out selectionRange);
        if (keyColours.Count > 2048) keyColours.Clear();
    }

    /// <summary>Draws one message's text with its top left at <paramref name="position"/>, and remembers where for the mouse.</summary>
    private void DrawMessageText(ImDrawListPtr drawList, ChatMessage message, int index, Vector2 position, float wrap, Vector4 colour, Vector4 background)
    {
        var layout = LayoutOf(message, wrap);
        var text = layout.Source;
        placements.Add(new Placement(message, index, position, layout, background));

        // The selection goes under the text.
        if (hasSelectionRange && selectionRange.Slice(index, text.Text.Length, out var from, out var to, out var continues))
        {
            scratchRects.Clear();
            layout.Highlight(from, to, continues, ImGui.GetFontSize() * 0.3f, scratchRects);
            var fill = ImGui.GetColorU32(ImGuiCol.TextSelectedBg);
            foreach (var (min, max) in scratchRects) drawList.AddRectFilled(position + min, position + max, fill);
        }

        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        var lineHeight = ImGui.GetTextLineHeight();
        foreach (var segment in layout.Segments)
        {
            var line = layout.Lines[segment.Line];
            var run = text.Runs[segment.Run];
            var x = position.X + layout.X[segment.Start];

            if (segment.IsObject)
            {
                var encoded = text.Objects[run.Object];
                var size = ObjectSize(encoded);
                DrawObject(drawList, encoded, new Vector2(x, position.Y + line.Top + ((line.Height - size.Y) * 0.5f)));
                continue;
            }

            var y = position.Y + line.Top + ((line.Height - lineHeight) * 0.5f);
            var runColour = RunColour(run, text, colour, background);
            drawList.AddText(font, fontSize, new Vector2(x, y), Painter.U32(runColour), text.Text.AsSpan(segment.Start, segment.End - segment.Start));

            // Web addresses are underlined all the time, the way they are everywhere else.
            if (run.Link >= 0 && text.Links[run.Link].Kind == LinkKind.Url)
            {
                var right = position.X + layout.X[segment.End - 1] + layout.Advance[segment.End - 1];
                drawList.AddLine(new Vector2(x, y + lineHeight - 1f), new Vector2(right, y + lineHeight - 1f),
                    Painter.U32(ColourMath.WithAlpha(runColour, runColour.W * 0.6f)), 1f);
            }
        }
    }

    private void DrawObject(ImDrawListPtr drawList, byte[] encoded, Vector2 position)
    {
        try
        {
            ImGuiHelpers.SeStringWrapped(encoded, new SeStringDrawParams
            {
                TargetDrawList = drawList,
                ScreenOffset = position,
                Font = ImGui.GetFont(),
                FontSize = ImGui.GetFontSize(),
                WrapWidth = float.MaxValue,
            });
        }
        catch (Exception ex)
        {
            ReportRichFailure(ex);
        }
    }

    /// <summary>The colour of one stretch of a message: a web link's, the game's colour for it, or the message's own.</summary>
    private Vector4 RunColour(TextRun run, RichText text, Vector4 colour, Vector4 background)
    {
        if (run.Link >= 0 && text.Links[run.Link].Kind == LinkKind.Url) return ColourMath.LegibleOn(palette.Accent, background, colour);
        return run.Colour == 0 ? colour : KeyColour(run.Colour, background, colour);
    }

    /// <summary>One of the game's numbered UI colours, from the column for the theme in use, made readable on the background.</summary>
    private Vector4 KeyColour(ushort key, Vector4 background, Vector4 fallback)
    {
        var themeIndex = theme.UiColourTheme(background);
        var cacheKey = ((long)ImGui.ColorConvertFloat4ToU32(background) << 24) | ((long)themeIndex << 16) | key;
        if (keyColours.TryGetValue(cacheKey, out var known)) return known;

        var rgba = GameLinks.UiColour(key, themeIndex);
        var colour = rgba == 0
            ? fallback
            : ColourMath.LegibleOn(new Vector4(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, 1f), background, fallback);
        keyColours[cacheKey] = colour;
        return colour;
    }

    // ------------------------------------------------------------------
    // The mouse
    // ------------------------------------------------------------------

    /// <summary>
    /// Takes the mouse over the message list once everything in it has been
    /// drawn: one invisible button the size of the list, placed after the
    /// controls drawn over it (scrollbar, jump button, "load earlier") so
    /// those still get their clicks.
    /// </summary>
    private void HandleTextMouse(Conversation conversation, Vector2 origin, float width, float height)
    {
        var io = ImGui.GetIO();
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##text", new Vector2(MathF.Max(1f, width), MathF.Max(1f, height)));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var mouse = io.MousePos;

        var under = hovered && !selecting ? TargetAt(mouse) : Target.None;

        if (ImGui.IsItemActivated())
        {
            pressAt = mouse;
            pressTarget = under;
            if (under.IsNone) StartSelection(mouse, io.KeyShift);
        }

        if (active)
        {
            // A press on a link that turns into a drag selects text after all.
            if (!pressTarget.IsNone && Vector2.Distance(mouse, pressAt) > io.MouseDragThreshold)
            {
                pressTarget = Target.None;
                StartSelection(pressAt, false);
            }

            if (selecting)
            {
                if (PointAt(mouse) is { } point) selection.Extend(point.Message, point.Index);

                // Dragging past the top or bottom edge scrolls the list.
                var edge = ImGui.GetTextLineHeight();
                if (mouse.Y < origin.Y) scroll.Wheel(MathF.Min(2f, (origin.Y - mouse.Y) / edge) * 0.25f, edge);
                else if (mouse.Y > origin.Y + height) scroll.Wheel(-MathF.Min(2f, (mouse.Y - origin.Y - height) / edge) * 0.25f, edge);
            }
        }

        if (ImGui.IsItemDeactivated())
        {
            if (!pressTarget.IsNone && pressTarget.Same(TargetAt(mouse))) Activate(pressTarget, conversation);
            else if (selecting && selection.IsEmpty) selection.Clear();
            selecting = false;
            pressTarget = Target.None;
        }

        if (!active && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right) && under.Link is { Kind: not LinkKind.Other } link)
        {
            menuLink = link;
            rightClickTaken = true;
            ImGui.OpenPopup(link.Kind == LinkKind.Item ? ItemMenu : LinkMenu);
        }

        ShowHover(under, hovered, mouse);
    }

    /// <summary>Puts one end of a selection where the mouse went down: a word on a double-click, a whole message on a triple.</summary>
    private void StartSelection(Vector2 at, bool extend)
    {
        selecting = false;
        if (PointAt(at) is not { } point)
        {
            selection.Clear();
            return;
        }

        var clicks = ImGui.GetMouseClickedCount(ImGuiMouseButton.Left);
        var text = Parsed(point.Message).Text;
        if (extend && !selection.IsEmpty)
        {
            selection.Extend(point.Message, point.Index);
            selecting = true;
        }
        else if (clicks == 2)
        {
            var (start, end) = TextLayout.WordAt(text, point.Index);
            selection.Select(point.Message, start, end);
        }
        else if (clicks >= 3)
        {
            selection.Select(point.Message, 0, text.Length);
        }
        else
        {
            selection.Start(point.Message, point.Index);
            selecting = true;
        }
    }

    /// <summary>Underlines and explains whatever clickable thing is under the cursor.</summary>
    private void ShowHover(Target under, bool hovered, Vector2 mouse)
    {
        if (!hovered) return;

        if (under.Name >= 0)
        {
            var spot = nameSpots[under.Name];
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.GetWindowDrawList().AddLine(new Vector2(spot.Min.X, spot.Max.Y), spot.Max, Painter.U32(spot.Colour), 1f);
            linkHovered = true;
            ImGui.SetTooltip($"Send a tell to {spot.Name}");
            return;
        }

        if (under.Link is not { } link || under.Placement < 0)
        {
            if (OverText(mouse)) ImGui.SetMouseCursor(ImGuiMouseCursor.TextInput);
            return;
        }

        var placement = placements[under.Placement];
        if (link.Kind != LinkKind.Other)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (link.Kind != LinkKind.Url)
            {
                scratchRects.Clear();
                placement.Layout.Underline(link, scratchRects);
                var colour = RunColour(placement.Layout.Source.Runs[placement.Layout.Source.RunAt(link.End - 1)], placement.Layout.Source, palette.Text, placement.Background);
                foreach (var (min, max) in scratchRects)
                {
                    ImGui.GetWindowDrawList().AddLine(placement.Origin + new Vector2(min.X, max.Y - 1f), placement.Origin + new Vector2(max.X, max.Y - 1f), Painter.U32(colour), 1f);
                }
            }
        }

        linkHovered = true;
        switch (link.Kind)
        {
            case LinkKind.Url:
                ImGui.SetTooltip($"{Host(link.Url!)}\nClick to open in your browser");
                break;

            case LinkKind.Item:
                HoverItem(link);
                break;

            case LinkKind.Map when link.Payload is MapLinkPayload map:
                ImGui.SetTooltip(MapLabel(map) + "\nClick to open the map");
                break;

            case LinkKind.Player:
                ImGui.SetTooltip($"Send a tell to {link.Name}");
                break;

            case LinkKind.PartyFinder:
                ImGui.SetTooltip("Click to see the party finder listing");
                break;
        }
    }

    /// <summary>Does what a click on a link or name does.</summary>
    private void Activate(Target target, Conversation conversation)
    {
        if (target.Name >= 0)
        {
            var spot = nameSpots[target.Name];
            plugin.OpenTell(spot.Name, spot.World);
            return;
        }

        switch (target.Link)
        {
            case { Kind: LinkKind.Url, Url: { } url }:
                plugin.OpenUrl(url);
                break;

            case { Kind: LinkKind.Item } item:
                menuLink = item;
                ImGui.OpenPopup(ItemMenu);
                break;

            case { Kind: LinkKind.Map, Payload: MapLinkPayload map }:
                Services.GameGui.OpenMapWithMapLink(map);
                break;

            case { Kind: LinkKind.Player, Name: { Length: > 0 } name } player when player.World != 0:
                plugin.OpenTell(name, player.World);
                break;

            case { Kind: LinkKind.PartyFinder } listing:
                GameLinks.OpenPartyFinder(listing.Id);
                break;
        }
    }

    /// <summary>The link or name under a point, if any.</summary>
    private Target TargetAt(Vector2 point)
    {
        for (var i = 0; i < nameSpots.Count; i++)
        {
            var spot = nameSpots[i];
            if (point.X >= spot.Min.X && point.X < spot.Max.X && point.Y >= spot.Min.Y && point.Y < spot.Max.Y) return new Target(-1, null, i);
        }

        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            if (point.Y < placement.Top || point.Y >= placement.Bottom) continue;
            var at = placement.Layout.CharAt(point - placement.Origin);
            if (at < 0) return Target.None;
            var link = placement.Layout.Source.LinkAt(at);
            return link == null ? Target.None : new Target(i, link, -1);
        }

        return Target.None;
    }

    private bool OverText(Vector2 point)
    {
        foreach (var placement in placements)
        {
            if (point.Y < placement.Top || point.Y >= placement.Bottom) continue;
            return point.X >= placement.Origin.X && point.X < placement.Origin.X + placement.Layout.Width + 4f;
        }
        return false;
    }

    /// <summary>
    /// The message and position nearest a point, for selecting. Between two
    /// messages is the start of the lower one; above everything on screen,
    /// the start of the first; below, the end of the last.
    /// </summary>
    private (ChatMessage Message, int Index)? PointAt(Vector2 point)
    {
        if (placements.Count == 0) return null;

        foreach (var placement in placements)
        {
            if (point.Y < placement.Top) return (placement.Message, 0);
            if (point.Y < placement.Bottom) return (placement.Message, placement.Layout.IndexAt(point - placement.Origin));
        }

        var last = placements[^1];
        return (last.Message, last.Layout.Source.Text.Length);
    }

    /// <summary>Copies the selection, if there is one. For Ctrl+C.</summary>
    private bool CopySelection()
    {
        if (selected == null || selection.IsEmpty) return false;
        var text = selection.Copy(selected.Messages, Parsed);
        if (text.Length == 0) return false;
        ImGui.SetClipboardText(text);
        return true;
    }

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private static string MapLabel(MapLinkPayload map)
    {
        try
        {
            return $"{map.PlaceName} {map.CoordinateString}";
        }
        catch (Exception)
        {
            return "A place on the map";
        }
    }

    // ------------------------------------------------------------------
    // The menu for a web link
    // ------------------------------------------------------------------

    private const string LinkMenu = "##linkmenu";

    private void DrawLinkMenu()
    {
        if (!BeginMenu(LinkMenu)) return;
        try
        {
            if (menuLink is not { Kind: LinkKind.Url, Url: { } url })
            {
                ImGui.CloseCurrentPopup();
                return;
            }

            ImGui.TextDisabled(Host(url));
            ImGui.Separator();
            if (ImGui.MenuItem("Open in your browser")) plugin.OpenUrl(url);
            if (ImGui.MenuItem("Copy link")) ImGui.SetClipboardText(url);
        }
        finally
        {
            EndMenu();
        }
    }
}
