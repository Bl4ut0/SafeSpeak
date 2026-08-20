using System.Speech.Synthesis;

namespace SafeSpeak.Core.Audio;

/// <summary>
/// Native offline Windows TTS engine utilizing System.Speech.Synthesis (SAPI5 / Windows Media Speech).
/// </summary>
public sealed class SystemSpeechTtsEngine : ITtsEngine
{
    private SpeechSynthesizer? _synthesizer;
    private readonly object _lock = new();

    public SystemSpeechTtsEngine()
    {
        InitializeSynthesizer();
    }

    private void InitializeSynthesizer()
    {
        lock (_lock)
        {
            _synthesizer?.Dispose();
            _synthesizer = new SpeechSynthesizer();
        }
    }

    public IReadOnlyList<VoiceInfo> GetAvailableVoices()
    {
        lock (_lock)
        {
            if (_synthesizer == null) return Array.Empty<VoiceInfo>();

            var list = new List<VoiceInfo>();
            try
            {
                foreach (var voice in _synthesizer.GetInstalledVoices())
                {
                    var info = voice.VoiceInfo;
                    list.Add(new VoiceInfo(
                        info.Name,
                        info.Name,
                        "Windows Desktop SAPI",
                        info.Culture.Name,
                        info.Gender.ToString(),
                        info.Description,
                        false
                    ));
                }
            }
            catch { }

            if (list.Count == 0)
            {
                list.Add(new VoiceInfo("Default", "Default System Voice", "System", "en-US", "Neutral", "Default Windows Voice", false));
            }

            return list;
        }
    }

    public Task SynthesizeToWaveStreamAsync(
        string text,
        Stream outputStream,
        string? voiceName = null,
        int rate = 0,
        int volume = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                using var synth = new SpeechSynthesizer();
                if (!string.IsNullOrEmpty(voiceName))
                {
                    try { synth.SelectVoice(voiceName); } catch { }
                }

                synth.Rate = Math.Clamp(rate, -10, 10);
                synth.Volume = Math.Clamp(volume, 0, 100);
                synth.SetOutputToWaveStream(outputStream);
                synth.Speak(text);
                synth.SetOutputToNull();
            }
        }, cancellationToken);
    }

    public Task SpeakDirectAsync(
        string text,
        string? voiceName = null,
        int rate = 0,
        int volume = 100,
        CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>();

        lock (_lock)
        {
            if (_synthesizer == null) InitializeSynthesizer();

            if (!string.IsNullOrEmpty(voiceName))
            {
                try { _synthesizer!.SelectVoice(voiceName); } catch { }
            }

            _synthesizer!.Rate = Math.Clamp(rate, -10, 10);
            _synthesizer.Volume = Math.Clamp(volume, 0, 100);
            _synthesizer.SetOutputToDefaultAudioDevice();

            void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e)
            {
                _synthesizer.SpeakCompleted -= OnSpeakCompleted;
                tcs.TrySetResult(true);
            }

            _synthesizer.SpeakCompleted += OnSpeakCompleted;

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    Stop();
                    tcs.TrySetCanceled();
                });
            }

            _synthesizer.SpeakAsync(text);
        }

        return tcs.Task;
    }

    public void Stop()
    {
        lock (_lock)
        {
            _synthesizer?.SpeakAsyncCancelAll();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _synthesizer?.Dispose();
            _synthesizer = null;
        }
    }
}
