using System.Numerics;
using Dalamud.Plugin.Services;
using Parley.Core;
using Parley.Core.Theme;

namespace Parley.Game;

/// <summary>
/// The colours and theme the player picked in the game's own configuration:
/// the UI theme from System Configuration, and the chat log colours from
/// Character Configuration.
///
/// Read by option name, because the chat colours for linkshell slots are a
/// numbered family ("ColorLS1" to "ColorLS8", and "ColorCWLS", then
/// "ColorCWLS2" to "ColorCWLS8"). Reads are cached for a couple of seconds:
/// they are cheap, but not so cheap they belong in a draw loop.
/// </summary>
internal sealed class GameColours
{
    private const long RefreshMs = 2000;

    private static readonly string[] LocalOptions = BuildNames("ColorLS", firstIsBare: false);
    private static readonly string[] CrossOptions = BuildNames("ColorCWLS", firstIsBare: true);

    private readonly IGameConfig gameConfig;
    private readonly Vector4[] local = new Vector4[ChannelGroups.SlotsPerGroup];
    private readonly Vector4[] cross = new Vector4[ChannelGroups.SlotsPerGroup];
    private Vector4 tell = GameThemes.DefaultTell;
    private Vector4 freeCompany = GameThemes.DefaultFreeCompany;
    private int theme;
    private long lastRead = -RefreshMs;

    public GameColours(IGameConfig gameConfig)
    {
        this.gameConfig = gameConfig;
        Array.Fill(local, GameThemes.DefaultLinkshell);
        Array.Fill(cross, GameThemes.DefaultCrossWorld);
    }

    /// <summary>Goes up whenever a read turns up something different, so dependants know to rebuild.</summary>
    public int Revision { get; private set; }

    /// <summary>0 Dark, 1 Light, 2 Classic FF, 3 Clear Blue, 4 Clear White, 5 Clear Green, 6 Clear Grey, 7 Clear Pink.</summary>
    public int Theme
    {
        get
        {
            Refresh();
            return theme;
        }
    }

    public Vector4 Tell
    {
        get
        {
            Refresh();
            return tell;
        }
    }

    /// <summary>Chat colour of a linkshell slot. Slot 0 (not in the linkshell any more) gets slot 1's.</summary>
    public Vector4 Slot(ChannelGroup group, int slot)
    {
        Refresh();
        if (group == ChannelGroup.FreeCompany) return freeCompany;

        var index = Math.Clamp(slot - 1, 0, ChannelGroups.SlotsPerGroup - 1);
        return group == ChannelGroup.CrossWorld ? cross[index] : local[index];
    }

    /// <summary>The colour for a whole tab group: tells, the free company, or the first slot of either linkshell kind.</summary>
    public Vector4 Group(ChannelGroup group) => group == ChannelGroup.Tell ? Tell : Slot(group, 1);

    private void Refresh()
    {
        var now = Environment.TickCount64;
        if (now - lastRead < RefreshMs) return;
        lastRead = now;

        var changed = false;
        try
        {
            if (gameConfig.System.TryGetUInt("ColorThemeType", out var themeType) && themeType < GameThemes.Count)
                changed |= Assign(ref theme, (int)themeType);

            changed |= Read("ColorTell", ref tell);
            changed |= Read("ColorFCompany", ref freeCompany);
            for (var i = 0; i < ChannelGroups.SlotsPerGroup; i++)
            {
                changed |= Read(LocalOptions[i], ref local[i]);
                changed |= Read(CrossOptions[i], ref cross[i]);
            }
        }
        catch (Exception ex)
        {
            // An option renamed by a patch must not take the chat window down
            // with it. The previous values, or the defaults, stay in use.
            Services.Log.Verbose(ex, "Could not read the game's colour settings.");
        }

        if (changed) Revision++;
    }

    private bool Read(string option, ref Vector4 colour)
    {
        if (!gameConfig.UiConfig.TryGetUInt(option, out var packed)) return false;

        // Stored as 0xRRGGBB. Zero is treated as "not set" rather than as
        // black, which nobody could read chat in anyway.
        var rgb = packed & 0xFFFFFF;
        if (rgb == 0) return false;

        var read = ColourMath.FromRgb(rgb);
        if (read == colour) return false;
        colour = read;
        return true;
    }

    /// <summary>"ColorLS1".."ColorLS8", or with <paramref name="firstIsBare"/> "ColorCWLS", "ColorCWLS2".."ColorCWLS8".</summary>
    private static string[] BuildNames(string prefix, bool firstIsBare)
    {
        var names = new string[ChannelGroups.SlotsPerGroup];
        for (var i = 0; i < names.Length; i++)
            names[i] = i == 0 && firstIsBare ? prefix : $"{prefix}{i + 1}";
        return names;
    }

    private static bool Assign(ref int field, int value)
    {
        if (field == value) return false;
        field = value;
        return true;
    }
}
