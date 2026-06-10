using System.IO;
using System.Media;

namespace OpenTranslate.Services;

public static class TranslationSoundService
{
    private const double Volume = 0.28;
    private const int SampleRate = 44100;

    public static void PlayTranslationStarted() => _ = Task.Run(PlayCore);

    private static void PlayCore()
    {
        try
        {
            var wav = BuildTwoToneWav(
                (523, 30),
                gapMs: 12,
                (659, 35));

            using var stream = new MemoryStream(wav);
            using var player = new SoundPlayer(stream);
            player.PlaySync();
        }
        catch
        {
            // Sin salida de audio disponible.
        }
    }

    private static byte[] BuildTwoToneWav(
        (int FrequencyHz, int DurationMs) first,
        int gapMs,
        (int FrequencyHz, int DurationMs) second)
    {
        var firstSamples = GenerateToneSamples(first.FrequencyHz, first.DurationMs);
        var gapSamples = new short[SampleRate * gapMs / 1000];
        var secondSamples = GenerateToneSamples(second.FrequencyHz, second.DurationMs);

        var pcm = new short[firstSamples.Length + gapSamples.Length + secondSamples.Length];
        firstSamples.CopyTo(pcm, 0);
        secondSamples.CopyTo(pcm, firstSamples.Length + gapSamples.Length);

        return WrapInWav(pcm);
    }

    private static short[] GenerateToneSamples(int frequencyHz, int durationMs)
    {
        var count = SampleRate * durationMs / 1000;
        var samples = new short[count];
        var fadeIn = SampleRate * 0.004;
        var fadeOut = SampleRate * 0.006;

        for (var i = 0; i < count; i++)
        {
            var t = (double)i / SampleRate;
            var attack = Math.Min(1.0, i / fadeIn);
            var release = Math.Min(1.0, (count - i) / fadeOut);
            var envelope = attack * release;
            var value = Math.Sin(2 * Math.PI * frequencyHz * t) * short.MaxValue * Volume * envelope;
            samples[i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        }

        return samples;
    }

    private static byte[] WrapInWav(short[] pcm)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var byteRate = SampleRate * channels * bitsPerSample / 8;
        var dataSize = pcm.Length * sizeof(short);
        var buffer = new byte[44 + dataSize];

        buffer[0] = (byte)'R'; buffer[1] = (byte)'I'; buffer[2] = (byte)'F'; buffer[3] = (byte)'F';
        WriteInt32(buffer, 4, 36 + dataSize);
        buffer[8] = (byte)'W'; buffer[9] = (byte)'A'; buffer[10] = (byte)'V'; buffer[11] = (byte)'E';
        buffer[12] = (byte)'f'; buffer[13] = (byte)'m'; buffer[14] = (byte)'t'; buffer[15] = (byte)' ';
        WriteInt32(buffer, 16, 16);
        WriteInt16(buffer, 20, 1);
        WriteInt16(buffer, 22, channels);
        WriteInt32(buffer, 24, SampleRate);
        WriteInt32(buffer, 28, byteRate);
        WriteInt16(buffer, 32, (short)(channels * bitsPerSample / 8));
        WriteInt16(buffer, 34, bitsPerSample);
        buffer[36] = (byte)'d'; buffer[37] = (byte)'a'; buffer[38] = (byte)'t'; buffer[39] = (byte)'a';
        WriteInt32(buffer, 40, dataSize);

        Buffer.BlockCopy(pcm, 0, buffer, 44, dataSize);
        return buffer;
    }

    private static void WriteInt16(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
