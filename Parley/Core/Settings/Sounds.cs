using System.Buffers.Binary;

namespace Parley.Core.Settings;

/// <summary>
/// The notification sounds Parley makes itself, and the volume control for
/// sound files. Everything here produces a complete 16-bit PCM WAV in memory,
/// ready for Windows to play; nothing is written anywhere.
///
/// Volume is applied to the samples rather than to the output device, which
/// would also turn down the game.
/// </summary>
public static class Sounds
{
    private const int SampleRate = 44100;

    /// <summary>A WAV of one of Parley's own sounds at the given volume, 0 to 100.</summary>
    public static byte[] Render(AlertSound sound, int volume)
    {
        var gain = Gain(volume);
        var samples = sound switch
        {
            AlertSound.Ping => Synthesise(0.28, t => Envelope(t, 0.004, 0.07) * Sine(1568, t)),
            AlertSound.Bell => Synthesise(1.2, t =>
                Envelope(t, 0.003, 0.35) * ((0.6 * Sine(660, t)) + (0.25 * Sine(660 * 2.76, t)) + (0.15 * Sine(660 * 5.4, t)))),
            AlertSound.Pop => Synthesise(0.12, t => Envelope(t, 0.002, 0.03) * Sine(620 - (2400 * t), t)),
            _ => Synthesise(0.7, t =>
                (Envelope(t, 0.004, 0.16) * 0.55 * Sine(880, t))
                + (Envelope(t - 0.09, 0.004, 0.2) * 0.45 * Sine(1320, t - 0.09))),
        };

        var pcm = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++)
            pcm[i] = (short)Math.Clamp(Math.Round(samples[i] * gain * 0.8 * short.MaxValue), short.MinValue, short.MaxValue);
        return Wav(pcm, channels: 1, SampleRate);
    }

    /// <summary>
    /// A copy of a WAV file at the given volume, 0 to 100. Only uncompressed
    /// 16-bit files are understood, which is what most tools save by default.
    /// </summary>
    /// <returns>Null, with the reason in <paramref name="error"/>, if the file is not one of those.</returns>
    public static byte[]? Scale(byte[] file, int volume, out string error)
    {
        error = string.Empty;
        var span = file.AsSpan();
        if (span.Length < 12 || !span[..4].SequenceEqual("RIFF"u8) || !span.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            error = "That is not a WAV file.";
            return null;
        }

        int channels = 0, rate = 0, bits = 0, format = 0;
        var dataStart = -1;
        var dataLength = 0;
        var at = 12;
        while (at + 8 <= span.Length)
        {
            var id = span.Slice(at, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(at + 4, 4));
            if (size < 0) break;
            var body = at + 8;

            if (id.SequenceEqual("fmt "u8) && size >= 16 && body + 16 <= span.Length)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(body + 2, 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(body + 14, 2));

                // WAVE_FORMAT_EXTENSIBLE carries the real format further in.
                if (format == 0xFFFE && size >= 26 && body + 26 <= span.Length)
                    format = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(body + 24, 2));
            }
            else if (id.SequenceEqual("data"u8))
            {
                dataStart = body;
                dataLength = Math.Min(size, span.Length - body);
                break;
            }

            // Chunks are padded to an even length.
            at = body + size + (size & 1);
        }

        if (format != 1 || bits != 16 || channels is < 1 or > 2 || rate <= 0)
        {
            error = "Only uncompressed 16-bit WAV files can be used.";
            return null;
        }

        if (dataStart < 0 || dataLength < 2)
        {
            error = "The file has no sound in it.";
            return null;
        }

        var gain = Gain(volume);
        var pcm = new short[dataLength / 2];
        for (var i = 0; i < pcm.Length; i++)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(dataStart + (i * 2), 2));
            pcm[i] = (short)Math.Clamp(Math.Round(sample * gain), short.MinValue, short.MaxValue);
        }

        return Wav(pcm, channels, rate);
    }

    /// <summary>A volume percentage as a multiplier. Squared, so the slider's middle sounds about half as loud.</summary>
    public static double Gain(int volume)
    {
        var fraction = Math.Clamp(volume, 0, 100) / 100.0;
        return fraction * fraction;
    }

    public static string Label(AlertSound sound) => sound switch
    {
        AlertSound.None => "None",
        AlertSound.Game => "Game sound effect",
        AlertSound.Chime => "Chime",
        AlertSound.Ping => "Ping",
        AlertSound.Bell => "Bell",
        AlertSound.Pop => "Pop",
        AlertSound.File => "Sound file",
        _ => sound.ToString(),
    };

    private static double[] Synthesise(double seconds, Func<double, double> wave)
    {
        var samples = new double[(int)(seconds * SampleRate)];
        for (var i = 0; i < samples.Length; i++) samples[i] = wave(i / (double)SampleRate);

        // A few milliseconds of fade at the very end, so nothing clicks.
        var fade = Math.Min(samples.Length, SampleRate / 200);
        for (var i = 0; i < fade; i++) samples[samples.Length - 1 - i] *= i / (double)fade;
        return samples;
    }

    private static double Sine(double frequency, double t) => t < 0 ? 0 : Math.Sin(2 * Math.PI * frequency * t);

    /// <summary>A quick rise, then an exponential fall with the given time constant.</summary>
    private static double Envelope(double t, double attack, double decay)
    {
        if (t < 0) return 0;
        var rise = t < attack ? t / attack : 1.0;
        return rise * Math.Exp(-Math.Max(0, t - attack) / decay);
    }

    private static byte[] Wav(short[] pcm, int channels, int rate)
    {
        var dataBytes = pcm.Length * 2;
        var wav = new byte[44 + dataBytes];
        var span = wav.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], rate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], rate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);

        for (var i = 0; i < pcm.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(span[(44 + (i * 2))..], pcm[i]);
        return wav;
    }
}
