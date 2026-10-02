using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Parley.Core.Settings;

namespace Parley.Ui;

/// <summary>
/// The fonts the chat window draws in, when that is not simply Dalamud's
/// default at its default size. Built through Dalamud's font atlas so each is
/// rasterised at the size asked for rather than scaled up and blurred. A
/// section of the window with a size of its own gets a font of its own, built
/// the first time it is asked for.
/// </summary>
internal sealed class FontManager : IDisposable
{
    /// <summary>Fonts by size, in hundredths of the default size. Null where building one failed.</summary>
    private readonly Dictionary<int, IFontHandle?> handles = [];
    private FontChoice builtChoice;
    private float builtScale = -1f;

    /// <summary>Goes up each time the font changes, so text measured in the old one is measured again.</summary>
    public int Generation { get; private set; }

    /// <summary>Starts again with the fonts the settings ask for. Cheap when nothing has changed.</summary>
    public void Apply(Configuration config)
    {
        if (builtScale >= 0f && config.Font == builtChoice && MathF.Abs(config.FontScale - builtScale) < 0.001f) return;

        DisposeHandles();
        builtChoice = config.Font;
        builtScale = config.FontScale;
        Generation++;
    }

    /// <summary>
    /// Makes the chat font current, at <paramref name="sectionScale"/> times
    /// the size set for text, until the result is disposed. Null while that
    /// font is still being built, or when Dalamud's default fits as it is.
    /// </summary>
    public IDisposable? Push(float sectionScale = 1f)
    {
        var scale = builtScale * sectionScale;
        if (builtChoice == FontChoice.Dalamud && MathF.Abs(scale - 1f) < 0.01f) return null;

        var key = (int)MathF.Round(scale * 100f);
        if (!handles.TryGetValue(key, out var handle))
        {
            handle = Build(builtChoice, key / 100f);
            handles[key] = handle;
        }
        return handle is { Available: true } ? handle.Push() : null;
    }

    public void Dispose() => DisposeHandles();

    private static IFontHandle? Build(FontChoice choice, float scale)
    {
        try
        {
            var builder = Services.PluginInterface.UiBuilder;
            var size = builder.FontDefaultSizePx * scale;
            return choice == FontChoice.GameAxis
                ? builder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, size))
                : builder.FontAtlas.NewDelegateFontHandle(toolkit => toolkit.OnPreBuild(pre => pre.AddDalamudDefaultFont(size)));
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Could not build the chat font; using Dalamud's default.");
            return null;
        }
    }

    private void DisposeHandles()
    {
        foreach (var handle in handles.Values) handle?.Dispose();
        handles.Clear();
    }
}
