using System.Media;
using SafeSpeak.Core.Audio;

namespace SafeSpeak.Core.Accessibility;

/// <summary>
/// Plays discrete system earcons and audio feedback for blind and low-vision streamers.
/// </summary>
public static class SoundCuePlayer
{
    public static void PlayCue(SoundCueType cueType)
    {
        Task.Run(() =>
        {
            try
            {
                switch (cueType)
                {
                    case SoundCueType.Armed:
                        Console.Beep(880, 80);
                        Console.Beep(1320, 100);
                        break;
                    case SoundCueType.Disarmed:
                        Console.Beep(1320, 80);
                        Console.Beep(880, 100);
                        break;
                    case SoundCueType.MessageApproved:
                        Console.Beep(1046, 50);
                        break;
                    case SoundCueType.TikFinityConnected:
                        Console.Beep(523, 70);
                        Console.Beep(659, 70);
                        Console.Beep(784, 90);
                        break;
                    case SoundCueType.TikFinityDisconnected:
                        Console.Beep(784, 70);
                        Console.Beep(659, 70);
                        Console.Beep(523, 90);
                        break;
                    case SoundCueType.EmergencyStop:
                        Console.Beep(1200, 120);
                        Console.Beep(600, 150);
                        break;
                    case SoundCueType.QueueEmpty:
                        SystemSounds.Asterisk.Play();
                        break;
                }
            }
            catch
            {
                // Fallback to system sounds if beep fails
                try { SystemSounds.Beep.Play(); } catch { }
            }
        });
    }

    public static async Task PlayCueAsync(
        SoundCueType cueType,
        IAudioRouter audioRouter,
        float volume = 1.0f,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioRouter);
        (int Frequency, int Duration)[] tones = cueType switch
        {
            SoundCueType.Armed => [(880, 80), (1320, 100)],
            SoundCueType.Disarmed => [(1320, 80), (880, 100)],
            SoundCueType.MessageApproved => [(1046, 50)],
            SoundCueType.TikFinityConnected => [(523, 70), (659, 70), (784, 90)],
            SoundCueType.TikFinityDisconnected => [(784, 70), (659, 70), (523, 90)],
            SoundCueType.EmergencyStop => [(1200, 120), (600, 150)],
            SoundCueType.QueueEmpty => [(880, 100)],
            _ => []
        };

        if (tones.Length == 0)
        {
            return;
        }

        using MemoryStream waveStream = CreateWaveStream(tones);
        await audioRouter.PlayWaveStreamAsync(
            waveStream,
            Math.Clamp(volume, 0f, 1.5f),
            cancellationToken).ConfigureAwait(false);
    }

    private static MemoryStream CreateWaveStream(
        IReadOnlyList<(int Frequency, int Duration)> tones)
    {
        const int sampleRate = 22050;
        const short channels = 1;
        const short bitsPerSample = 16;
        const int gapMilliseconds = 18;
        int totalSamples = tones.Sum(tone => sampleRate * tone.Duration / 1000) +
            Math.Max(0, tones.Count - 1) * sampleRate * gapMilliseconds / 1000;
        int dataLength = totalSamples * sizeof(short);
        var stream = new MemoryStream(44 + dataLength);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataLength);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * bitsPerSample / 8);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write("data"u8.ToArray());
            writer.Write(dataLength);

            for (int toneIndex = 0; toneIndex < tones.Count; toneIndex++)
            {
                (int frequency, int duration) = tones[toneIndex];
                int samples = sampleRate * duration / 1000;
                for (int sample = 0; sample < samples; sample++)
                {
                    double envelope = Math.Min(1d, sample / (sampleRate * 0.008d));
                    envelope *= Math.Min(1d, (samples - sample) / (sampleRate * 0.012d));
                    short value = (short)(Math.Sin(2d * Math.PI * frequency * sample / sampleRate) *
                        short.MaxValue * 0.28d * envelope);
                    writer.Write(value);
                }

                if (toneIndex < tones.Count - 1)
                {
                    int gapSamples = sampleRate * gapMilliseconds / 1000;
                    for (int sample = 0; sample < gapSamples; sample++) writer.Write((short)0);
                }
            }
        }

        stream.Position = 0;
        return stream;
    }
}
