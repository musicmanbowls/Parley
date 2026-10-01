using System.Runtime.InteropServices;
using Parley.Core.Settings;

namespace Parley.Game;

/// <summary>
/// Plays notification sounds: the game's own chat sounds through the game,
/// and Parley's sounds and sound files through Windows, at a volume of their
/// own. Framework thread only.
/// </summary>
internal sealed partial class AlertPlayer : IDisposable
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;

    /// <summary>Sounds already rendered, by what they were rendered from, so a busy channel costs nothing after the first.</summary>
    private readonly Dictionary<(AlertSound Sound, int Volume, string File, long Stamp), byte[]> rendered = [];

    /// <summary>
    /// The sound Windows is playing right now, copied to memory the garbage
    /// collector will not move. It has to stay put until playback ends, which
    /// is why it is only freed once the next sound has stopped it.
    /// </summary>
    private nint playing;

    public void Dispose()
    {
        Stop();
        rendered.Clear();
    }

    /// <returns>Why nothing played, or null if it did (or was meant to be silent).</returns>
    public string? Play(ChannelAlert alert)
    {
        switch (alert.Sound)
        {
            case AlertSound.None:
                return null;

            case AlertSound.Game:
                GameSound.PlayChatEffect(alert.GameSound);
                return null;
        }

        var wav = Render(alert, out var error);
        if (wav == null) return error;

        Stop();
        playing = Marshal.AllocHGlobal(wav.Length);
        Marshal.Copy(wav, 0, playing, wav.Length);
        return PlaySound(playing, 0, SndMemory | SndAsync | SndNoDefault) ? null : "Windows would not play the sound.";
    }

    private byte[]? Render(ChannelAlert alert, out string? error)
    {
        error = null;
        var file = alert.Sound == AlertSound.File ? alert.File.Trim() : string.Empty;

        // A file is re-read if it has been changed since it was last played.
        var stamp = 0L;
        if (file.Length > 0)
        {
            try
            {
                stamp = File.GetLastWriteTimeUtc(file).Ticks;
            }
            catch (Exception)
            {
                stamp = 0;
            }
        }

        var key = (alert.Sound, alert.Volume, file, stamp);
        if (rendered.TryGetValue(key, out var cached)) return cached;

        byte[]? wav;
        if (alert.Sound == AlertSound.File)
        {
            if (file.Length == 0)
            {
                error = "No sound file has been chosen.";
                return null;
            }

            try
            {
                var info = new FileInfo(file);
                if (!info.Exists)
                {
                    error = "The sound file was not found.";
                    return null;
                }

                if (info.Length > 16 * 1024 * 1024)
                {
                    error = "That sound file is too large for a notification.";
                    return null;
                }

                wav = Sounds.Scale(File.ReadAllBytes(file), alert.Volume, out var reason);
                if (wav == null)
                {
                    error = reason;
                    return null;
                }
            }
            catch (Exception ex)
            {
                error = $"The sound file could not be read: {ex.Message}";
                return null;
            }
        }
        else
        {
            wav = Sounds.Render(alert.Sound, alert.Volume);
        }

        // Settings change rarely; a handful of entries is all this ever holds.
        if (rendered.Count > 32) rendered.Clear();
        rendered[key] = wav;
        return wav;
    }

    private void Stop()
    {
        // Stops whatever this process is playing through PlaySound, and does
        // not return until it has, after which the memory is safe to free.
        PlaySound(0, 0, 0);
        if (playing == 0) return;

        Marshal.FreeHGlobal(playing);
        playing = 0;
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(nint sound, nint module, uint flags);
}
