using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Parley.Core.History;
using Parley.Ui;

namespace Parley.UiHarness;

/// <summary>
/// An ImGui context with Dalamud's look, a plugin to draw, and the means to
/// poke at it: a mouse, a keyboard, and a camera.
/// </summary>
internal sealed unsafe class Stage : IDisposable
{
    private const float FontSize = 17f;
    private const float FrameSeconds = 1f / 60f;

    /// <summary>Filled in by ImGui's own assertion hook. Anything here is a misuse of ImGui.</summary>
    private static readonly List<string> NativeAsserts = [];

    private readonly ImGuiContextPtr context;
    private readonly Dictionary<ulong, Texture> textures = [];
    private readonly ScreenReader reader;
    private readonly Dictionary<Window, bool> wasOpen = [];
    private readonly Dictionary<Window, (Vector2 Pos, Vector2 Size)> drawnAt = [];
    private readonly string outputDirectory;
    private readonly float scale;
    private Vector2 mouse = new(-1f, -1f);
    private ImDrawDataPtr drawData;

    public Stage(string outputDirectory, string assetDirectory, float scale = 1f, HistoryStore? history = null, int width = 1040, int height = 640)
    {
        this.outputDirectory = outputDirectory;
        this.scale = scale;
        Width = width;
        Height = height;

        context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);
        igCustom_SetAssertCallback((delegate* unmanaged[Cdecl]<byte*, byte*, int, void>)&OnAssert);

        var io = ImGui.GetIO();
        io.IniFilename = null;

        // Copying goes to a string here, not to the real clipboard of whoever
        // is running the tests.
        Clipboard = string.Empty;
        io.Handle->SetClipboardTextFn = (void*)(delegate* unmanaged[Cdecl]<void*, byte*, void>)&OnSetClipboard;
        io.Handle->GetClipboardTextFn = (void*)(delegate* unmanaged[Cdecl]<void*, byte*>)&OnGetClipboard;
        io.DisplaySize = new Vector2(Width, Height);
        io.DeltaTime = FrameSeconds;
        io.FontGlobalScale = scale;

        // Dalamud builds its fonts at the scaled size and scales them back
        // down, so text stays sharp at any UI scale. Same here.
        // Latin, punctuation, and the symbol blocks the game's own font draws
        // from (arrows, shapes, circled numbers, card suits, CJK brackets).
        var text = AddFont(io, Path.Combine(assetDirectory, "NotoSansCJKjp-Medium.otf"),
            [0x0020, 0x00FF, 0x2000, 0x206F, 0x2100, 0x21FF, 0x2200, 0x22FF, 0x2460, 0x24FF, 0x2500, 0x27BF, 0x3000, 0x30FF, 0xFF00, 0xFFEF, 0]);
        var icons = AddFont(io, Path.Combine(assetDirectory, "FontAwesomeFreeSolid.otf"), [0xE000, 0xF8FF, 0]);
        if (!io.Fonts.Build()) throw new InvalidOperationException("The font atlas could not be built.");

        var ids = new List<ulong>();
        for (var i = 0; i < io.Fonts.Textures.Size; i++)
        {
            byte* data;
            int textureWidth, textureHeight;
            io.Fonts.GetTexDataAsRGBA32(i, &data, &textureWidth, &textureHeight);

            var copy = new uint[textureWidth * textureHeight];
            new ReadOnlySpan<uint>(data, copy.Length).CopyTo(copy);
            var id = (ulong)(i + 1);
            textures[id] = new Texture(textureWidth, textureHeight, copy);
            io.Fonts.SetTexID(i, new ImTextureID(id));
            ids.Add(id);
        }

        reader = new ScreenReader(io.Fonts.TexWidth, io.Fonts.TexHeight);
        reader.Register(text, icons: false, ids);
        reader.Register(icons, icons: true, ids);
        UiBuilder.IconFont = icons;

        DalamudStyle.Apply(scale);

        Clock = new DateTimeOffset(2026, 9, 30, 20, 15, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 30, 20, 15, 0))).ToUnixTimeMilliseconds();
        Plugin = new Plugin(history, () => Clock);
        Place(Plugin.MainWindow, new Vector2(16f, 16f));
        Place(Plugin.SettingsWindow, new Vector2(420f, 40f));
        foreach (var popOut in Plugin.PopOuts) Place(popOut, new Vector2(400f, 80f));
    }

    public Plugin Plugin { get; }

    /// <summary>Size of the screen the windows are drawn on, in pixels.</summary>
    public int Width { get; }
    public int Height { get; }

    /// <summary>Windows drawn after the plugin's own, such as stand-ins for Dalamud's for the install guide.</summary>
    public List<Window> Extra { get; } = [];

    /// <summary>The time the plugin believes it is, in Unix milliseconds. Moves on a frame at a time.</summary>
    public long Clock { get; set; }

    /// <summary>The text on screen as of the last frame.</summary>
    public List<TextRun> Text { get; private set; } = [];

    /// <summary>Everything that has gone wrong so far: ImGui assertions, exceptions out of a window, and logged errors.</summary>
    public List<string> Problems { get; } = [];

    public int FrameCount { get; private set; }

    /// <summary>Runs inside every frame, after the windows have drawn. For looking at ImGui's state while it is live.</summary>
    public Action? DuringFrame { get; set; }

    /// <summary>What ImGui thinks is going on with the pointer and keyboard. Only meaningful from <see cref="DuringFrame"/>.</summary>
    public static string Probe()
    {
        var g = ImGui.GetCurrentContext();
        var io = ImGui.GetIO();
        var hovered = g.HoveredWindow.IsNull ? "none" : Marshal.PtrToStringUTF8((nint)g.HoveredWindow.Handle->Name) ?? "?";
        var focused = g.NavWindow.IsNull ? "none" : Marshal.PtrToStringUTF8((nint)g.NavWindow.Handle->Name) ?? "?";
        return $"mouse=({io.MousePos.X:0},{io.MousePos.Y:0}) wheel={io.MouseWheel} hovered=[{hovered}] focused=[{focused}] "
               + $"activeId={g.ActiveId:X8} allowOverlap={g.ActiveIdAllowOverlap} hoveredId={g.HoveredId:X8}";
    }

    public void Dispose()
    {
        igCustom_SetAssertCallback(null);
        ImGui.DestroyContext(context);
    }

    // ------------------------------------------------------------------
    // Frames
    // ------------------------------------------------------------------

    public void Frame()
    {
        Clock += 16;
        Plugin.Deliver();
        Plugin.Tick();

        ImGui.GetIO().DeltaTime = FrameSeconds;
        ImGui.NewFrame();
        Host(Plugin.MainWindow);
        foreach (var popOut in Plugin.PopOuts) Host(popOut);
        Host(Plugin.SettingsWindow);
        foreach (var window in Extra)
        {
            if (!wasOpen.ContainsKey(window)) wasOpen[window] = false;
            Host(window);
        }
        DuringFrame?.Invoke();
        ImGui.Render();

        drawData = ImGui.GetDrawData();
        Text = reader.Read(drawData);
        FrameCount++;

        if (NativeAsserts.Count > 0)
        {
            foreach (var assert in NativeAsserts) Problems.Add($"frame {FrameCount}: ImGui assertion: {assert}");
            NativeAsserts.Clear();
        }

        var log = Services.Log.Problems;
        if (log.Count > 0)
        {
            foreach (var entry in log) Problems.Add($"frame {FrameCount}: logged: {entry}");
            log.Clear();
        }
    }

    public void Frames(int count)
    {
        for (var i = 0; i < count; i++) Frame();
    }

    /// <summary>Writes the last frame to a PNG in the output directory.</summary>
    public string Shot(string name) => Save(name, 0, 0, Width, Height, checker: true);

    /// <summary>
    /// Writes just one window from the last frame, with a small margin, on a
    /// plain background: the kind of picture that goes in a guide.
    /// </summary>
    public string ShotOf(string name, Window window, float margin = 12f)
    {
        if (!drawnAt.TryGetValue(window, out var rect)) throw new InvalidOperationException($"{window.WindowName} was not drawn.");

        var x = (int)MathF.Max(0f, rect.Pos.X - margin);
        var y = (int)MathF.Max(0f, rect.Pos.Y - margin);
        var right = (int)MathF.Min(Width, rect.Pos.X + rect.Size.X + margin);
        var bottom = (int)MathF.Min(Height, rect.Pos.Y + rect.Size.Y + margin);
        return Save(name, x, y, right - x, bottom - y, checker: false);
    }

    private string Save(string name, int x, int y, int width, int height, bool checker)
    {
        var canvas = new Canvas(Width, Height);
        if (checker) canvas.ClearChecker(new Vector3(0.20f, 0.24f, 0.30f), new Vector3(0.23f, 0.27f, 0.33f), 16);
        else canvas.Clear(new Vector3(0.84f, 0.86f, 0.89f));
        canvas.Draw(drawData, textures);

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, name + ".png");
        canvas.SavePng(path, x, y, width, height);
        return path;
    }

    // ------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------

    public void MoveTo(Vector2 position)
    {
        mouse = position;
        ImGui.GetIO().AddMousePosEvent(position.X, position.Y);
        Frame();
    }

    public void Click(Vector2 position, int button = 0)
    {
        MoveTo(position);
        Frame();
        ImGui.GetIO().AddMouseButtonEvent(button, true);
        Frame();
        ImGui.GetIO().AddMouseButtonEvent(button, false);
        Frames(2);
    }

    public void DoubleClick(Vector2 position)
    {
        MoveTo(position);
        Frame();
        for (var i = 0; i < 2; i++)
        {
            ImGui.GetIO().AddMouseButtonEvent(0, true);
            Frame();
            ImGui.GetIO().AddMouseButtonEvent(0, false);
            Frame();
        }
        Frame();
    }

    public void Drag(Vector2 from, Vector2 to, int steps = 6)
    {
        MoveTo(from);
        Frame();
        ImGui.GetIO().AddMouseButtonEvent(0, true);
        Frame();
        for (var i = 1; i <= steps; i++) MoveTo(Vector2.Lerp(from, to, i / (float)steps));
        ImGui.GetIO().AddMouseButtonEvent(0, false);
        Frames(2);
    }

    /// <summary>Turns the wheel with the cursor where it is. Positive is away from the user, which scrolls up.</summary>
    public void Wheel(float notches)
    {
        ImGui.GetIO().AddMouseWheelEvent(0f, notches);
        Frames(2);
    }

    public void Type(string text)
    {
        ImGui.GetIO().AddInputCharacters(text);
        Frames(2);
    }

    public void Press(ImGuiKey key)
    {
        ImGui.GetIO().AddKeyEvent(key, true);
        Frame();
        ImGui.GetIO().AddKeyEvent(key, false);
        Frames(2);
    }

    /// <summary>Presses a key with modifiers held, the way a person would: modifiers down first, up last.</summary>
    public void Chord(ImGuiKey key, bool alt = false, bool shift = false, bool ctrl = false)
    {
        var io = ImGui.GetIO();
        if (alt) io.AddKeyEvent(ImGuiKey.ModAlt, true);
        if (shift) io.AddKeyEvent(ImGuiKey.ModShift, true);
        if (ctrl) io.AddKeyEvent(ImGuiKey.ModCtrl, true);
        Frame();
        io.AddKeyEvent(key, true);
        Frame();
        io.AddKeyEvent(key, false);
        Frame();
        if (alt) io.AddKeyEvent(ImGuiKey.ModAlt, false);
        if (shift) io.AddKeyEvent(ImGuiKey.ModShift, false);
        if (ctrl) io.AddKeyEvent(ImGuiKey.ModCtrl, false);
        Frames(2);
    }

    // ------------------------------------------------------------------
    // Looking at the screen
    // ------------------------------------------------------------------

    /// <summary>The visible run containing this text, or null.</summary>
    public TextRun? Find(string text, int occurrence = 0)
    {
        foreach (var run in Text)
        {
            if (run.Icon || !run.Visible || !run.Text.Contains(text, StringComparison.Ordinal)) continue;
            if (occurrence-- == 0) return run;
        }
        return null;
    }

    /// <summary>The visible run that says exactly this, or null. For text that also appears inside something longer.</summary>
    public TextRun? FindExact(string text, int occurrence = 0)
    {
        foreach (var run in Text)
        {
            if (run.Icon || !run.Visible || run.Text != text) continue;
            if (occurrence-- == 0) return run;
        }
        return null;
    }

    public TextRun NeedExact(string text, int occurrence = 0) =>
        FindExact(text, occurrence) ?? throw new InvalidOperationException($"\"{text}\" is not on screen as a piece of text on its own.");

    public TextRun? FindIcon(Dalamud.Interface.FontAwesomeIcon icon, int occurrence = 0)
    {
        var glyph = ((char)icon).ToString();
        foreach (var run in Text)
        {
            if (!run.Icon || !run.Visible || run.Text != glyph) continue;
            if (occurrence-- == 0) return run;
        }
        return null;
    }

    public TextRun Need(string text, int occurrence = 0) =>
        Find(text, occurrence) ?? throw new InvalidOperationException($"\"{text}\" is not on screen.");

    public TextRun NeedIcon(Dalamud.Interface.FontAwesomeIcon icon, int occurrence = 0) =>
        FindIcon(icon, occurrence) ?? throw new InvalidOperationException($"The {icon} icon is not on screen.");

    public void ClickText(string text, int occurrence = 0, int button = 0) => Click(Need(text, occurrence).Centre, button);

    public void ClickIcon(Dalamud.Interface.FontAwesomeIcon icon, int occurrence = 0) => Click(NeedIcon(icon, occurrence).Centre);

    // ------------------------------------------------------------------
    // Hosting
    // ------------------------------------------------------------------

    public void Place(Window window, Vector2 position)
    {
        window.Position = position * scale;
        window.PositionCondition = ImGuiCond.FirstUseEver;
        wasOpen[window] = false;
    }

    /// <summary>Does for one window what Dalamud's window system does each frame, in the same order.</summary>
    private void Host(Window window)
    {
        window.PreOpenCheck();

        if (!window.IsOpen)
        {
            if (wasOpen[window])
            {
                wasOpen[window] = false;
                window.OnClose();
                window.IsFocused = false;
                window.IsHovered = false;
            }
            return;
        }

        window.Update();
        if (!window.DrawConditions()) return;

        if (!wasOpen[window])
        {
            wasOpen[window] = true;
            window.OnOpen();
        }

        window.PreDraw();

        if (window.Position.HasValue) ImGui.SetNextWindowPos(window.Position.Value, window.PositionCondition);
        if (window.Size.HasValue) ImGui.SetNextWindowSize(window.Size.Value * scale, window.SizeCondition);
        if (window.SizeConstraints.HasValue)
            ImGui.SetNextWindowSizeConstraints(window.SizeConstraints.Value.MinimumSize * scale, window.SizeConstraints.Value.MaximumSize * scale);
        if (window.BgAlpha.HasValue) ImGui.SetNextWindowBgAlpha(window.BgAlpha.Value);

        if (window.RequestFocus)
        {
            ImGui.SetNextWindowFocus();
            window.RequestFocus = false;
        }

        var open = true;
        if (ImGui.Begin(window.WindowName, ref open, window.Flags))
        {
            try
            {
                window.Draw();
            }
            catch (Exception ex)
            {
                // Dalamud replaces the window with an error panel at this point.
                Problems.Add($"frame {FrameCount + 1}: {window.WindowName} threw out of Draw: {ex}");
            }
        }

        if (!open) window.IsOpen = false;

        drawnAt[window] = (ImGui.GetWindowPos(), ImGui.GetWindowSize());
        window.IsFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        window.IsHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);
        ImGui.End();

        window.PostDraw();
    }

    private ImFontPtr AddFont(ImGuiIOPtr io, string path, ReadOnlySpan<ushort> ranges)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("A font Dalamud ships was not found.", path);

        // The atlas reads the ranges when it is built, so they have to outlive this call.
        var kept = (ushort*)NativeMemory.Alloc((nuint)ranges.Length, sizeof(ushort));
        ranges.CopyTo(new Span<ushort>(kept, ranges.Length));

        var font = io.Fonts.AddFontFromFileTTF(path, FontSize * scale, default, kept);
        font.Handle->Scale = 1f / scale;
        return font;
    }

    /// <summary>What was last copied.</summary>
    public static string Clipboard { get; set; } = string.Empty;

    private static nint clipboardBuffer;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSetClipboard(void* userData, byte* text) => Clipboard = Marshal.PtrToStringUTF8((nint)text) ?? string.Empty;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte* OnGetClipboard(void* userData)
    {
        if (clipboardBuffer != 0) Marshal.FreeCoTaskMem(clipboardBuffer);
        clipboardBuffer = Marshal.StringToCoTaskMemUTF8(Clipboard);
        return (byte*)clipboardBuffer;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnAssert(byte* expression, byte* file, int line)
    {
        var where = Path.GetFileName(Marshal.PtrToStringAnsi((nint)file) ?? "?");
        NativeAsserts.Add($"{Marshal.PtrToStringAnsi((nint)expression)} ({where}:{line})");
    }

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern void igCustom_SetAssertCallback(delegate* unmanaged[Cdecl]<byte*, byte*, int, void> callback);
}
