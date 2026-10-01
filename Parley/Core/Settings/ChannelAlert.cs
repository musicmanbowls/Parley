using System.Text.Json.Serialization;

namespace Parley.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<AlertSound>))]
public enum AlertSound
{
    None,

    /// <summary>One of the sixteen sounds &lt;se.1&gt; to &lt;se.16&gt; play in chat. Plays at the game's own volume.</summary>
    Game,

    // Sounds Parley makes itself, so their volume can be set.
    Chime,
    Ping,
    Bell,
    Pop,

    /// <summary>A WAV file of the user's choosing.</summary>
    File,
}

/// <summary>What happens when a message arrives in one kind of conversation.</summary>
public sealed class ChannelAlert
{
    public AlertSound Sound { get; set; }

    /// <summary>Which of the game's chat sounds, 1 to 16. Used when <see cref="Sound"/> is <see cref="AlertSound.Game"/>.</summary>
    public int GameSound { get; set; } = 1;

    /// <summary>Full path of a WAV file. Used when <see cref="Sound"/> is <see cref="AlertSound.File"/>.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>0 to 100. Applies to Parley's own sounds and to files; the game's sounds follow the game's volume.</summary>
    public int Volume { get; set; } = 60;

    /// <summary>Show a Dalamud notification with the start of the message.</summary>
    public bool Toast { get; set; }

    /// <summary>Whether this sound's loudness is Parley's to set.</summary>
    [JsonIgnore]
    public bool HasVolume => Sound is not (AlertSound.None or AlertSound.Game);

    public void Clamp()
    {
        if (!Enum.IsDefined(Sound)) Sound = AlertSound.None;
        GameSound = Math.Clamp(GameSound, 1, 16);
        Volume = Math.Clamp(Volume, 0, 100);
        File ??= string.Empty;
    }
}
