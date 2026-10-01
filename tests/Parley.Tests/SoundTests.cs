using System.Buffers.Binary;
using Parley.Core.Settings;
using Xunit;

namespace Parley.Tests;

public class SoundTests
{
    private static short[] Samples(byte[] wav)
    {
        var length = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40, 4));
        var samples = new short[length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + (i * 2), 2));
        return samples;
    }

    private static int Peak(byte[] wav) => Samples(wav).Max(sample => Math.Abs((int)sample));

    /// <summary>A 16-bit WAV with whatever the test wants in it, and an extra chunk before the data like many editors write.</summary>
    private static byte[] MakeWav(short[] samples, int channels = 1, int rate = 22050, int bits = 16, int format = 1, bool oddChunk = true)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)format);
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * channels * bits / 8);
        writer.Write((short)(channels * bits / 8));
        writer.Write((short)bits);
        if (oddChunk)
        {
            // Three bytes long, so padded to four.
            writer.Write("LIST"u8);
            writer.Write(3);
            writer.Write(new byte[] { 1, 2, 3, 0 });
        }
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var sample in samples) writer.Write(sample);
        writer.Flush();

        var bytes = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), bytes.Length - 8);
        return bytes;
    }

    [Theory]
    [InlineData(AlertSound.Chime)]
    [InlineData(AlertSound.Ping)]
    [InlineData(AlertSound.Bell)]
    [InlineData(AlertSound.Pop)]
    public void Each_of_parleys_sounds_is_a_playable_wav(AlertSound sound)
    {
        var wav = Sounds.Render(sound, 80);

        Assert.True(wav.AsSpan(0, 4).SequenceEqual("RIFF"u8));
        Assert.True(wav.AsSpan(8, 4).SequenceEqual("WAVE"u8));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20, 2)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34, 2)));
        Assert.Equal(wav.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4, 4)));
        Assert.Equal(wav.Length - 44, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40, 4)));
        Assert.True(Peak(wav) > 1000, "the sound is audible");

        // Short enough to be a notification, and starting and ending quietly so nothing clicks.
        var samples = Samples(wav);
        Assert.InRange(samples.Length / 44100.0, 0.05, 1.5);
        Assert.True(Math.Abs((int)samples[0]) < 2000);
        Assert.True(Math.Abs((int)samples[^1]) < 200);
    }

    [Fact]
    public void Volume_makes_the_sound_quieter_and_nothing_at_zero()
    {
        var loud = Peak(Sounds.Render(AlertSound.Chime, 100));
        var half = Peak(Sounds.Render(AlertSound.Chime, 50));
        var silent = Peak(Sounds.Render(AlertSound.Chime, 0));

        Assert.True(half < loud);
        Assert.InRange(half / (double)loud, 0.2, 0.3);
        Assert.Equal(0, silent);
        Assert.True(loud < short.MaxValue, "full volume leaves some headroom rather than clipping");
    }

    [Fact]
    public void A_wav_file_is_played_at_the_chosen_volume()
    {
        var file = MakeWav([10000, -10000, 20000, -32768, 0]);

        var full = Sounds.Scale(file, 100, out var error);
        var half = Sounds.Scale(file, 50, out _);

        Assert.NotNull(full);
        Assert.Equal(string.Empty, error);
        Assert.Equal([10000, -10000, 20000, -32768, 0], Samples(full!));
        Assert.Equal([2500, -2500, 5000, -8192, 0], Samples(half!));
    }

    [Fact]
    public void A_wav_file_keeps_its_channels_and_rate()
    {
        var file = MakeWav([1, 2, 3, 4], channels: 2, rate: 48000);

        var scaled = Sounds.Scale(file, 100, out _)!;

        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(scaled.AsSpan(22, 2)));
        Assert.Equal(48000, BinaryPrimitives.ReadInt32LittleEndian(scaled.AsSpan(24, 4)));
        Assert.Equal(4, Samples(scaled).Length);
    }

    [Theory]
    [InlineData(8, 1, "16-bit")]
    [InlineData(24, 1, "16-bit")]
    [InlineData(16, 3, "16-bit")]
    public void Wav_files_it_cannot_play_are_refused_with_a_reason(int bits, int format, string reason)
    {
        var file = MakeWav([1, 2, 3], bits: bits, format: format);

        Assert.Null(Sounds.Scale(file, 100, out var error));
        Assert.Contains(reason, error);
    }

    [Fact]
    public void Something_that_is_not_a_wav_is_refused()
    {
        Assert.Null(Sounds.Scale("ID3 this is an mp3"u8.ToArray(), 100, out var error));
        Assert.Contains("not a WAV", error);

        Assert.Null(Sounds.Scale([], 100, out _));
    }

    [Fact]
    public void A_wav_with_no_sound_in_it_is_refused()
    {
        Assert.Null(Sounds.Scale(MakeWav([]), 100, out var error));
        Assert.Contains("no sound", error);
    }

    [Fact]
    public void Volume_is_squared_so_the_middle_of_the_slider_sounds_about_half_as_loud()
    {
        Assert.Equal(1.0, Sounds.Gain(100));
        Assert.Equal(0.25, Sounds.Gain(50));
        Assert.Equal(0.0, Sounds.Gain(0));
        Assert.Equal(1.0, Sounds.Gain(250));
        Assert.Equal(0.0, Sounds.Gain(-3));
    }
}
