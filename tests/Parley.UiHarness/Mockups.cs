using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace Parley.UiHarness;

/// <summary>
/// Stand-ins for the two Dalamud windows the install guide walks through.
/// Dalamud's own windows need the game to run, so these copy their layout,
/// wording and style closely enough to recognise, and mark the controls the
/// guide's steps refer to with numbered rings.
///
/// The wording comes from Dalamud's source at the version this was built
/// against: SettingsTabExperimental, DevPluginsSettingsEntry,
/// ThirdRepoSettingsEntry, DalamudComponents.FloatingActionButtons and
/// PluginInstallerWindow.
/// </summary>
internal static class Callouts
{
    private static readonly Vector4 Ring = new(1f, 0.62f, 0.11f, 1f);

    /// <summary>Rings the last item drawn and puts a number beside it.</summary>
    public static void Mark(int number, float grow = 4f, bool numberOnRight = false) =>
        Mark(number, ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), grow, numberOnRight);

    public static void Mark(int number, Vector2 min, Vector2 max, float grow = 4f, bool numberOnRight = false)
    {
        var scale = ImGui.GetIO().FontGlobalScale;
        var draw = ImGui.GetForegroundDrawList();
        var colour = ImGui.ColorConvertFloat4ToU32(Ring);
        grow *= scale;
        min -= new Vector2(grow, grow);
        max += new Vector2(grow, grow);
        draw.AddRect(min, max, colour, 6f * scale, ImDrawFlags.None, 3f * scale);

        var radius = 11f * scale;
        var centre = new Vector2(
            numberOnRight ? max.X + radius + (4f * scale) : min.X - radius - (4f * scale),
            min.Y + ((max.Y - min.Y) * 0.5f));
        draw.AddCircleFilled(centre, radius, colour, 24);
        var label = number.ToString();
        var size = ImGui.CalcTextSize(label);
        draw.AddText(centre - (size * 0.5f), 0xFF1A1A1A, label);
    }
}

/// <summary>
/// Dalamud Settings, on the Experimental tab, with Parley added either as a
/// dev plugin or, with <see cref="RepoUrl"/> set, through its plugin list.
/// </summary>
internal sealed class MockDalamudSettings : Window
{
    private static readonly Vector4 Grey = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 Changed = new(0f, 0.8f, 0.13f, 1f);
    private static readonly Vector4 Attention = new(1f, 0.709f, 0f, 1f);

    public MockDalamudSettings(string? repoUrl = null)
        : base("Dalamud Settings###mockDalamudSettings", ImGuiWindowFlags.NoCollapse)
    {
        RepoUrl = repoUrl;
        Size = new Vector2(740, repoUrl == null ? 560 : 640);
        SizeCondition = ImGuiCond.Always;
    }

    /// <summary>Where the guide tells people to put the plugin.</summary>
    public string Path { get; set; } = @"C:\FFXIV Plugins\Parley\Plugin\Parley.dll";

    /// <summary>The plugin list's address, being added as a custom repository. Null for the dev plugin picture.</summary>
    public string? RepoUrl { get; }

    public override void Draw()
    {
        var scale = ImGui.GetIO().FontGlobalScale;

        var search = string.Empty;
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search for settings...", ref search, 100);

        if (!ImGui.BeginTabBar("##tabs")) return;
        foreach (var tab in new[] { "General", "Look & Feel", "Auto-Updates", "Server Info Bar", "Badges", "Experimental", "About" })
        {
            if (!ImGui.BeginTabItem(tab, tab == "Experimental" ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None)) continue;
            if (tab == "Experimental" && RepoUrl != null) DrawCustomRepositories(scale, RepoUrl);
            else if (tab == "Experimental") DrawExperimental(scale);
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    /// <summary>The Experimental tab as most people see it, developer mode off, with a repository being added.</summary>
    private static void DrawCustomRepositories(float scale, string url)
    {
        ImGui.Dummy(new Vector2(1f, 6f * scale));

        var testing = false;
        ImGui.Checkbox("Get plugin testing builds", ref testing);
        ImGui.TextColored(Grey, "Receive testing prereleases for selected plugins.");
        ImGui.Dummy(new Vector2(1f, 4f * scale));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(1f, 4f * scale));

        var developer = false;
        ImGui.Checkbox("Enable Developer Mode", ref developer);
        ImGui.TextColored(Grey, "Unlocks developer-specific settings.");
        ImGui.Dummy(new Vector2(1f, 8f * scale));

        ImGui.TextUnformatted("Custom Plugin Repositories");
        ImGui.SameLine();
        ImGui.TextColored(Changed, "(Changed)");
        ImGui.TextColored(Grey, "Add custom plugin repositories.");
        ImGui.Dummy(new Vector2(1f, 2f * scale));

        ImGui.PushStyleColor(ImGuiCol.Text, Attention);
        ImGui.PushFont(Parley.Ui.UiBuilder.IconFont);
        ImGui.TextUnformatted(FontAwesomeIcon.ExclamationTriangle.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextUnformatted("READ THIS FIRST!");
        ImGui.PopStyleColor();
        ImGui.TextWrapped("We cannot take any responsibility for custom plugins and repositories.");
        ImGui.TextWrapped("If someone told you to copy/paste something here, it's very possible that you are being scammed or taken advantage of.");
        ImGui.TextWrapped("Plugins have full control over your PC, like any other program, and may cause harm or crashes.");
        ImGui.TextWrapped("Please make absolutely sure that you only install plugins from developers you trust.");
        ImGui.Button("Ok, I have read and understood this warning");
        Callouts.Mark(1);
        ImGui.Dummy(new Vector2(1f, 7f * scale));

        // The same column sizes Dalamud gives its list.
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.Columns(4, "##repos");
        ImGui.SetColumnWidth(0, 18 + (5 * scale));
        ImGui.SetColumnWidth(1, width - (18 + 16 + 14) - ((5 + 45 + 26) * scale));
        ImGui.SetColumnWidth(2, 16 + (45 * scale));
        ImGui.SetColumnWidth(3, 14 + (26 * scale));
        ImGui.Separator();
        foreach (var heading in new[] { "#", "URL", "Enabled", string.Empty })
        {
            ImGui.TextUnformatted(heading);
            ImGui.NextColumn();
        }
        ImGui.Separator();

        ImGui.TextUnformatted("0");
        ImGui.NextColumn();
        ImGui.TextUnformatted("XIVLauncher");
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.Separator();

        ImGui.TextUnformatted("1");
        ImGui.NextColumn();
        var typed = url;
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##url", ref typed, 300);
        Callouts.Mark(2, grow: 3f);
        ImGui.NextColumn();
        ImGui.NextColumn();
        IconButton(FontAwesomeIcon.Plus);
        Callouts.Mark(3, grow: 3f, numberOnRight: true);
        ImGui.NextColumn();
        ImGui.Columns(1);

        DrawSaveButtons(scale);
    }

    private void DrawExperimental(float scale)
    {
        ImGui.Dummy(new Vector2(1f, 6f * scale));

        var testing = false;
        ImGui.Checkbox("Get plugin testing builds", ref testing);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Receive testing prereleases for selected plugins.");
        ImGui.Dummy(new Vector2(1f, 4f * scale));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(1f, 4f * scale));

        var developer = true;
        ImGui.Checkbox("Enable Developer Mode", ref developer);
        Callouts.Mark(1);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Unlocks developer-specific settings.");
        ImGui.Dummy(new Vector2(1f, 8f * scale));

        ImGui.TextUnformatted("Dev Plugin Locations");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0f, 0.8f, 0.13f, 1f), "(Changed)");
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Add dev plugin load locations.\nThis must be a path to the plugin DLL.");

        IconButtonWithText(FontAwesomeIcon.Folder, "Select Dev Plugin DLL");
        Callouts.Mark(2);
        ImGui.Dummy(new Vector2(1f, 5f * scale));

        var width = ImGui.GetContentRegionAvail().X;
        ImGui.Columns(5, "##locations");
        ImGui.SetColumnWidth(0, 18 + (5 * scale));
        ImGui.SetColumnWidth(1, width - (18 + 16 + 14 + 14) - ((5 + 45 + 26 + 120) * scale));
        ImGui.SetColumnWidth(2, 16 + (120 * scale));
        ImGui.SetColumnWidth(3, 16 + (45 * scale));
        ImGui.SetColumnWidth(4, 14 + (26 * scale));
        ImGui.Separator();
        foreach (var heading in new[] { "#", "Path", "Nickname", "Enabled", string.Empty })
        {
            ImGui.TextUnformatted(heading);
            ImGui.NextColumn();
        }
        ImGui.Separator();

        ImGui.TextUnformatted("1");
        ImGui.NextColumn();
        var path = Path;
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##path", ref path, 300);
        var pathMin = ImGui.GetItemRectMin();
        var pathMax = ImGui.GetItemRectMax();
        ImGui.NextColumn();
        var nickname = string.Empty;
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##nick", "Optional...", ref nickname, 64);
        ImGui.NextColumn();
        var enabled = true;
        ImGui.Checkbox("##enabled", ref enabled);
        var enabledMin = ImGui.GetItemRectMin();
        var enabledMax = ImGui.GetItemRectMax();
        ImGui.NextColumn();
        IconButton(FontAwesomeIcon.Trash);
        ImGui.NextColumn();
        ImGui.Separator();

        ImGui.TextUnformatted("2");
        ImGui.NextColumn();
        var empty = string.Empty;
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##next", ref empty, 300);
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.Columns(1);

        Callouts.Mark(3, pathMin, new Vector2(enabledMax.X, MathF.Max(pathMax.Y, enabledMax.Y)));
        _ = enabledMin;

        DrawSaveButtons(scale);
    }

    /// <summary>The round buttons that float at the bottom right of Dalamud's settings.</summary>
    private static void DrawSaveButtons(float scale)
    {
        var window = ImGui.GetWindowSize();
        ImGui.PushFont(Parley.Ui.UiBuilder.IconFont);
        var closeWidth = ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X + (32f * scale);
        ImGui.PopFont();
        var saveWidth = IconButtonWithTextWidth(FontAwesomeIcon.Save, "Save") + (32f * scale);

        ImGui.SetCursorPos(window - new Vector2(closeWidth + saveWidth + (8f * scale) + (74f * scale), 74f * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(16, 12) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 100f);
        IconButton(FontAwesomeIcon.Times);
        ImGui.SameLine();
        IconButtonWithText(FontAwesomeIcon.Save, "Save");
        Callouts.Mark(4, numberOnRight: true);
        ImGui.PopStyleVar(2);
    }

    internal static bool IconButton(FontAwesomeIcon icon)
    {
        ImGui.PushFont(Parley.Ui.UiBuilder.IconFont);
        var clicked = ImGui.Button(icon.ToIconString());
        ImGui.PopFont();
        return clicked;
    }

    internal static bool IconButtonWithText(FontAwesomeIcon icon, string text)
    {
        var style = ImGui.GetStyle();
        ImGui.PushFont(Parley.Ui.UiBuilder.IconFont);
        var iconSize = ImGui.CalcTextSize(icon.ToIconString());
        ImGui.PopFont();
        var textSize = ImGui.CalcTextSize(text);

        var start = ImGui.GetCursorScreenPos();
        var size = new Vector2(iconSize.X + textSize.X + (style.FramePadding.X * 2f) + style.ItemInnerSpacing.X,
            MathF.Max(iconSize.Y, textSize.Y) + (style.FramePadding.Y * 2f));
        var clicked = ImGui.Button($"##{text}", size);

        var draw = ImGui.GetWindowDrawList();
        var colour = ImGui.GetColorU32(ImGuiCol.Text);
        ImGui.PushFont(Parley.Ui.UiBuilder.IconFont);
        draw.AddText(start + new Vector2(style.FramePadding.X, (size.Y - iconSize.Y) * 0.5f), colour, icon.ToIconString());
        ImGui.PopFont();
        draw.AddText(start + new Vector2(style.FramePadding.X + iconSize.X + style.ItemInnerSpacing.X, (size.Y - textSize.Y) * 0.5f), colour, text);
        return clicked;
    }

    private static float IconButtonWithTextWidth(FontAwesomeIcon icon, string text)
    {
        ImGui.PushFont(Parley.Ui.UiBuilder.IconFont);
        var iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X;
        ImGui.PopFont();
        return iconWidth + ImGui.CalcTextSize(text).X + ImGui.GetStyle().ItemInnerSpacing.X;
    }
}

/// <summary>
/// Dalamud's Plugin Installer with Parley in the list: under Installed Dev
/// Plugins, or, given a version to install, found by searching All Plugins.
/// </summary>
internal sealed class MockPluginInstaller : Window
{
    public MockPluginInstaller(string? installVersion = null)
        : base("Plugin Installer###mockInstaller", ImGuiWindowFlags.NoCollapse)
    {
        InstallVersion = installVersion;
        Size = new Vector2(760, 440);
        SizeCondition = ImGuiCond.Always;
    }

    public bool Enabled { get; set; }

    /// <summary>The version on the Install button when Parley comes from its plugin list. Null for the dev plugin picture.</summary>
    public string? InstallVersion { get; }

    private bool FromRepo => InstallVersion != null;

    public override void Draw()
    {
        var scale = ImGui.GetIO().FontGlobalScale;

        var search = FromRepo ? "Parley" : string.Empty;
        ImGui.SetNextItemWidth(260f * scale);
        ImGui.InputTextWithHint("##search", "Search", ref search, 100);
        var searchMin = ImGui.GetItemRectMin();
        var searchMax = ImGui.GetItemRectMax();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180f * scale);
        if (ImGui.BeginCombo("Sort By", "Alphabetical")) ImGui.EndCombo();
        ImGui.Dummy(new Vector2(1f, 4f * scale));

        var height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() - (6f * scale);
        ImGui.BeginChild("##categories", new Vector2(190f * scale, height), true);
        ImGui.Selectable("All Plugins", FromRepo);
        var allMin = ImGui.GetItemRectMin();
        var allMax = ImGui.GetItemRectMax();
        ImGui.Selectable("Installed Plugins");
        var devMin = Vector2.Zero;
        var devMax = Vector2.Zero;
        if (!FromRepo)
        {
            ImGui.Dummy(new Vector2(1f, 4f * scale));
            ImGui.TextDisabled("Dev Tools");
            ImGui.Indent(10f * scale);
            ImGui.Selectable("Installed Dev Plugins", true);
            devMin = ImGui.GetItemRectMin();
            devMax = ImGui.GetItemRectMax();
            ImGui.Unindent(10f * scale);
        }
        ImGui.Dummy(new Vector2(1f, 4f * scale));
        ImGui.Selectable("Changelog");
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##plugins", new Vector2(0f, height), true);
        DrawEntry(scale);
        ImGui.EndChild();

        if (FromRepo)
        {
            Callouts.Mark(1, allMin, allMax, 2f);
            Callouts.Mark(2, searchMin, searchMax, 3f);
        }
        else
        {
            Callouts.Mark(1, devMin, devMax, 2f);
        }

        ImGui.Dummy(new Vector2(1f, 2f * scale));
        ImGui.Button("Settings");
        ImGui.SameLine(ImGui.GetWindowWidth() - (90f * scale));
        ImGui.Button("Close");
    }

    private void DrawEntry(float scale)
    {
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var rowHeight = (FromRepo ? 196f : 100f) * scale;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + new Vector2(width, rowHeight), ImGui.GetColorU32(ImGuiCol.Header), 4f * scale);

        // Parley's icon: a speech bubble on a disc, as Dalamud shows a placeholder for plugins without one.
        var icon = new Vector2(64f, 64f) * scale;
        var iconAt = start + new Vector2(10f * scale, FromRepo ? 14f * scale : (rowHeight - icon.Y) * 0.5f);
        ParleyIcon.Draw(draw, iconAt, icon.X);

        var textLeft = iconAt.X + icon.X + (12f * scale);
        var line = ImGui.GetTextLineHeight();
        draw.AddText(new Vector2(textLeft, start.Y + (10f * scale)), ImGui.GetColorU32(ImGuiCol.Text), "Parley");
        if (!FromRepo) draw.AddText(new Vector2(textLeft + ImGui.CalcTextSize("Parley").X, start.Y + (10f * scale)), ImGui.GetColorU32(ImGuiCol.TextDisabled), " (dev plugin)");
        draw.AddText(new Vector2(textLeft, start.Y + (10f * scale) + line), ImGui.GetColorU32(ImGuiCol.TextDisabled), " by MusicManBowls");
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textLeft, start.Y + (14f * scale) + (line * 2f)), ImGui.GetColorU32(ImGuiCol.Text),
            "A tabbed messenger for tells, your free company, linkshells and cross-world linkshells.", width - (textLeft - start.X) - (70f * scale));

        if (FromRepo)
        {
            // Opened up, as it is after a click: the description, then the button that installs it.
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(textLeft, start.Y + (24f * scale) + (line * 4f)), ImGui.GetColorU32(ImGuiCol.TextDisabled),
                "Keeps every tell, your free company, and each linkshell and cross-world linkshell in its own conversation.", width - (textLeft - start.X) - (20f * scale));
            ImGui.SetCursorScreenPos(new Vector2(textLeft, start.Y + rowHeight - ImGui.GetFrameHeight() - (14f * scale)));
            ImGui.Button($"Install v{InstallVersion}");
            Callouts.Mark(3, grow: 3f, numberOnRight: true);
            ImGui.SetCursorScreenPos(start);
            ImGui.Dummy(new Vector2(width, rowHeight));
            return;
        }

        // The on/off switch Dalamud draws at the right of each installed plugin.
        var toggle = new Vector2(40f, 22f) * scale;
        var toggleAt = new Vector2(start.X + width - toggle.X - (14f * scale), start.Y + (12f * scale));
        var on = Enabled;
        draw.AddRectFilled(toggleAt, toggleAt + toggle, on ? 0xFF3B8A2A : 0xFF5A5A5A, toggle.Y * 0.5f);
        var knob = on ? toggleAt.X + toggle.X - (toggle.Y * 0.5f) : toggleAt.X + (toggle.Y * 0.5f);
        draw.AddCircleFilled(new Vector2(knob, toggleAt.Y + (toggle.Y * 0.5f)), (toggle.Y * 0.5f) - (3f * scale), 0xFFEEEEEE, 20);
        Callouts.Mark(2, toggleAt, toggleAt + toggle, 5f);

        ImGui.Dummy(new Vector2(width, rowHeight));
    }
}

/// <summary>
/// Parley's icon, drawn with shapes rather than a font glyph so it stays sharp
/// at any size: a speech bubble with three dots, on Parley's wine red.
/// </summary>
internal static class ParleyIcon
{
    private const uint Background = 0xFF3A2A6B;
    private const uint Bubble = 0xFFFFFFFF;

    public static void Draw(ImDrawListPtr draw, Vector2 at, float size, bool rounded = true)
    {
        draw.AddRectFilled(at, at + new Vector2(size, size), Background, rounded ? size * 0.14f : 0f);

        var bubbleMin = at + new Vector2(size * 0.17f, size * 0.20f);
        var bubbleMax = at + new Vector2(size * 0.83f, size * 0.66f);
        draw.AddRectFilled(bubbleMin, bubbleMax, Bubble, size * 0.13f);

        // The tail, under the bubble's left side.
        draw.AddTriangleFilled(
            at + new Vector2(size * 0.27f, size * 0.62f),
            at + new Vector2(size * 0.45f, size * 0.62f),
            at + new Vector2(size * 0.24f, size * 0.81f),
            Bubble);

        var middle = (bubbleMin.Y + bubbleMax.Y) * 0.5f;
        for (var i = -1; i <= 1; i++)
            draw.AddCircleFilled(new Vector2(at.X + (size * 0.5f) + (i * size * 0.16f), middle), size * 0.052f, Background, 32);
    }
}

/// <summary>Nothing but the icon, filling the window, for the picture Dalamud shows beside the plugin.</summary>
internal sealed class IconWindow : Window
{
    public IconWindow(float size)
        : base("##parleyIcon", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground)
    {
        Size = new Vector2(size, size);
        SizeCondition = ImGuiCond.Always;
    }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw() => ImGui.PopStyleVar(2);

    public override void Draw() => ParleyIcon.Draw(ImGui.GetWindowDrawList(), ImGui.GetWindowPos(), ImGui.GetWindowSize().X, rounded: false);
}
