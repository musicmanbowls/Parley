using System.Numerics;
using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Parley.Core.Theme;

namespace Parley.Ui;

/// <summary>
/// The window's frame, so it can look like the game's chat log: the title bar
/// and border can go, the background can fade out softly towards the edges,
/// and the whole window can fade back while it is not in use.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>How long fading back takes, and coming forward again, in seconds.</summary>
    private const float FadeOutSeconds = 0.8f;
    private const float FadeInSeconds = 0.15f;

    /// <summary>0 while in use, 1 once fully faded.</summary>
    private float fade;
    private long lastActive;
    private int lastActivityStamp;

    /// <summary>The background's opacity this frame, which the soft background is drawn with.</summary>
    private float backgroundAlpha = 1f;

    /// <summary>Style variables pushed in PreDraw for the frame, popped in PostDraw.</summary>
    private int lookPushes;

    /// <summary>From PreDraw: this frame's flags, border, background opacity and fade.</summary>
    private void ApplyLook()
    {
        var faded = UpdateFade();
        var opacity = Lerp(config.WindowOpacity, MathF.Min(config.WindowOpacity, config.IdleOpacity), faded);
        var textAlpha = Lerp(1f, config.IdleTextOpacity, faded);
        backgroundAlpha = theme.Palette.WindowBg.W * opacity;

        // ImGui's own alpha also dims the background it draws, so that is
        // allowed for in the background's opacity.
        if (config.SoftEdges) BgAlpha = null;
        else if (theme.Themed) BgAlpha = MathF.Min(1f, backgroundAlpha / textAlpha);
        else BgAlpha = opacity < 0.999f || textAlpha < 0.999f ? MathF.Min(1f, opacity / textAlpha) : null;

        var flags = openQuietly ? BaseFlags | ImGuiWindowFlags.NoFocusOnAppearing : BaseFlags;
        if (!config.ShowTitleBar) flags |= ImGuiWindowFlags.NoTitleBar;
        if (config.SoftEdges) flags |= ImGuiWindowFlags.NoBackground;
        Flags = flags;

        lookPushes = 0;
        if (config.SoftEdges || !config.WindowBorder)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            lookPushes++;
        }
        if (textAlpha < 0.999f)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, textAlpha);
            lookPushes++;
        }
    }

    private void PopLook()
    {
        if (lookPushes > 0) ImGui.PopStyleVar(lookPushes);
        lookPushes = 0;
    }

    /// <summary>
    /// How far faded the window is. It fades once nothing has happened for the
    /// time set: the mouse not over it, no keyboard in it, and nothing new to
    /// read. Any of those brings it straight back.
    /// </summary>
    private float UpdateFade()
    {
        if (!config.FadeWhenIdle)
        {
            fade = 0f;
            return 0f;
        }

        var tick = Environment.TickCount64;
        var stamp = ActivityStamp();
        if (lastActive == 0 || IsHovered || IsFocused || stamp != lastActivityStamp) lastActive = tick;
        lastActivityStamp = stamp;

        var idle = tick - lastActive > config.FadeAfterSeconds * 1000L;
        var step = ImGui.GetIO().DeltaTime;
        fade = idle ? MathF.Min(1f, fade + (step / FadeOutSeconds)) : MathF.Max(0f, fade - (step / FadeInSeconds));
        return fade;
    }

    /// <summary>Changes whenever something new arrives that the window shows: a line in General's tab, or a message anywhere unread.</summary>
    private int ActivityStamp()
    {
        if (ShowingGeneral)
        {
            var lines = plugin.General.Shown(config.GeneralTab);
            return lines.Count > 0 ? RuntimeHelpers.GetHashCode(lines[^1]) : 0;
        }

        var newest = selected is { Messages.Count: > 0 } shown ? RuntimeHelpers.GetHashCode(shown.Messages[^1]) : 0;
        return HashCode.Combine(newest, store.TotalUnread);
    }

    /// <summary>
    /// The background, solid in the middle and fading out over the last few
    /// pixels to each edge, in place of the hard-edged one ImGui draws. Drawn
    /// first, under everything else in the window.
    /// </summary>
    private void DrawSoftBackground()
    {
        var drawList = ImGui.GetWindowDrawList();
        var position = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();

        // Below the title bar, when there is one: ImGui has drawn that already.
        var top = config.ShowTitleBar ? ImGui.GetFrameHeight() : 0f;
        var min = position + new Vector2(0f, top);
        var max = position + size;
        if (max.X - min.X < 2f || max.Y - min.Y < 2f) return;

        var soft = MathF.Min(config.SoftEdgeWidth * ImGuiHelpers.GlobalScale, MathF.Min(max.X - min.X, max.Y - min.Y) * 0.5f);
        var solid = ImGui.ColorConvertFloat4ToU32(ColourMath.WithAlpha(theme.Palette.WindowBg, backgroundAlpha));
        var clear = ImGui.ColorConvertFloat4ToU32(ColourMath.WithAlpha(theme.Palette.WindowBg, 0f));
        var innerMin = min + new Vector2(soft, soft);
        var innerMax = max - new Vector2(soft, soft);

        drawList.AddRectFilled(innerMin, innerMax, solid);

        // The four sides, fading outwards. Colours go upper left, upper right, lower right, lower left.
        drawList.AddRectFilledMultiColor(new Vector2(innerMin.X, min.Y), new Vector2(innerMax.X, innerMin.Y), clear, clear, solid, solid);
        drawList.AddRectFilledMultiColor(new Vector2(innerMin.X, innerMax.Y), new Vector2(innerMax.X, max.Y), solid, solid, clear, clear);
        drawList.AddRectFilledMultiColor(new Vector2(min.X, innerMin.Y), new Vector2(innerMin.X, innerMax.Y), clear, solid, solid, clear);
        drawList.AddRectFilledMultiColor(new Vector2(innerMax.X, innerMin.Y), new Vector2(max.X, innerMax.Y), solid, clear, clear, solid);

        // The corners, solid only where they meet the middle.
        drawList.AddRectFilledMultiColor(min, innerMin, clear, clear, solid, clear);
        drawList.AddRectFilledMultiColor(new Vector2(innerMax.X, min.Y), new Vector2(max.X, innerMin.Y), clear, clear, clear, solid);
        drawList.AddRectFilledMultiColor(innerMax, max, solid, clear, clear, clear);
        drawList.AddRectFilledMultiColor(new Vector2(min.X, innerMax.Y), new Vector2(innerMin.X, max.Y), clear, solid, clear, clear);
    }

    /// <summary>
    /// Standing in for the game's chat log, the window is out of sight when
    /// that would be (cutscenes, group pose, loading screens, the UI hidden)
    /// unless Enter has just called it up to type in, until the keyboard goes
    /// elsewhere again.
    /// </summary>
    public override bool DrawConditions()
    {
        if (!plugin.ReplacingGameChat || !plugin.GameChatWouldHide()) return true;
        if (!summoned) return false;

        if (Environment.TickCount64 - summonedAt > SummonGraceMs && !IsFocused)
        {
            summoned = false;
            return false;
        }
        return true;
    }

    /// <summary>Long enough for a window called up to take the keyboard before its losing it counts.</summary>
    private const long SummonGraceMs = 500;

    private bool summoned;
    private long summonedAt;

    private void Summon()
    {
        summoned = true;
        summonedAt = Environment.TickCount64;
    }

    private static float Lerp(float from, float to, float amount) => from + ((to - from) * amount);
}
