using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Parley.Core.Settings;

namespace Parley.Ui;

/// <summary>
/// The font the chat window draws in, when that is not simply Dalamud's
/// default at its default size. Built through Dalamud's font atlas so it is
/// rasterised at the size asked for rather than scaled up and blurred.
/// </summary>
internal sealed class FontManager : IDisposable
{
    private IFontHandle? handle;
    private FontChoice builtChoice;
    private float builtScale = -1f;

    /// <summary>Goes up each time the font changes, so text measured in the old one is measured again.</summary>
    public int Generation { get; private set; }

    /// <summary>Rebuilds the font if the settings ask for a different one. Cheap when they do not.</summary>
    public void Apply(Configuration config)
    {
        if (builtScale >= 0f && config.Font == builtChoice && MathF.Abs(config.FontScale - builtScale) < 0.001f) return;

        handle?.Dispose();
        handle = null;
        builtChoice = config.Font;
        builtScale = config.FontScale;
        Generation++;

        // The default font at the default size needs no handle of its own.
        if (config.Font == FontChoice.Dalamud && MathF.Abs(config.FontScale - 1f) < 0.01f) return;

        try
        {
            var builder = Services.PluginInterface.UiBuilder;
            var size = builder.FontDefaultSizePx * config.FontScale;
            handle = config.Font == FontChoice.GameAxis
                ? builder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, size))
                : builder.FontAtlas.NewDelegateFontHandle(toolkit => toolkit.OnPreBuild(pre => pre.AddDalamudDefaultFont(size)));
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Could not build the chat font; using Dalamud's default.");
            handle = null;
        }
    }

    /// <summary>
    /// Makes the chat font current until the result is disposed. Null while
    /// the font is still being built, or when the default is in use.
    /// </summary>
    public IDisposable? Push() => handle is { Available: true } ? handle.Push() : null;

    public void Dispose()
    {
        handle?.Dispose();
        handle = null;
    }
}
