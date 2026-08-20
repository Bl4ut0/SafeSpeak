using System.Speech.Synthesis;
using SafeSpeak.Core.Audio.VoiceFramework;

namespace SafeSpeak.Core.Audio;

/// <summary>
/// Unified modular TTS engine aggregating Windows Natural Voices (OneCore),
/// Local Neural ONNX voice packs, and SAPI5 voices.
/// </summary>
public sealed class ModularTtsEngine : ITtsEngine
{
    private SpeechSynthesizer? _synthesizer;
    private readonly LocalNeuralVoiceManager _neuralVoiceManager;
    private readonly object _lock = new();

    private readonly VoicePackageManager _packageManager;

    public LocalNeuralVoiceManager NeuralVoiceManager => _neuralVoiceManager;
    public VoicePackageManager PackageManager => _packageManager;

    public ModularTtsEngine(LocalNeuralVoiceManager? neuralVoiceManager = null, VoicePackageManager? packageManager = null)
    {
        _neuralVoiceManager = neuralVoiceManager ?? new LocalNeuralVoiceManager();
        _packageManager = packageManager ?? new VoicePackageManager();

        // Unlock modern Windows 10/11 OneCore natural voices
        OneCoreVoiceBridge.UnlockOneCoreVoices();

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
            var list = new List<VoiceInfo>();

            // 1. Scan Custom Imported User Voice Packs (.safespeak-voice)
            foreach (var pack in _packageManager.GetInstalledPackages())
            {
                list.Add(new VoiceInfo(
                    pack.Manifest.Id,
                    $"👑 [Custom] {pack.Manifest.DisplayName} (by {pack.Manifest.Author})",
                    "Custom Voice Pack",
                    pack.Manifest.Culture,
                    pack.Manifest.Gender,
                    pack.Manifest.Description,
                    true
                ));
            }

            // 2. Scan Local Neural ONNX voices in %LocalAppData%\SafeSpeak\Voices
            foreach (var catalogItem in LocalNeuralVoiceManager.AvailableCatalog)
            {
                if (_neuralVoiceManager.IsVoiceInstalled(catalogItem.Id))
                {
                    list.Add(new VoiceInfo(
                        catalogItem.Id,
                        $"🌟 [Local Neural] {catalogItem.DisplayName}",
                        "Piper Neural ONNX",
                        catalogItem.Culture,
                        catalogItem.Gender,
                        catalogItem.Description,
                        true
                    ));
                }
            }

            // 2. Scan Windows SAPI & OneCore Natural Voices
            if (_synthesizer != null)
            {
                try
                {
                    foreach (var voice in _synthesizer.GetInstalledVoices())
                    {
                        var info = voice.VoiceInfo;
                        bool isOneCore = info.Name.Contains("OneCore", StringComparison.OrdinalIgnoreCase) ||
                                         info.Description.Contains("OneCore", StringComparison.OrdinalIgnoreCase) ||
                                         info.Name.Contains("Natural", StringComparison.OrdinalIgnoreCase);

                        string prefix = isOneCore ? "✨ [Natural]" : "🖥️ [System]";
                        string provider = isOneCore ? "Windows OneCore Natural" : "Windows Desktop SAPI";

                        list.Add(new VoiceInfo(
                            info.Name,
                            $"{prefix} {info.Name}",
                            provider,
                            info.Culture.Name,
                            info.Gender.ToString(),
                            info.Description,
                            isOneCore
                        ));
                    }
                }
                catch { }
            }

            // Sort: Natural Neural voices first, then standard desktop voices
            var sortedList = list.OrderByDescending(v => v.IsNaturalNeural).ThenBy(v => v.DisplayName).ToList();

            if (sortedList.Count == 0)
            {
                sortedList.Add(new VoiceInfo("Default", "Default System Voice", "System", "en-US", "Neutral", "Default Windows Voice", false));
            }

            return sortedList;
        }
    }

    public Task SynthesizeToWaveStreamAsync(
        string text,
        Stream outputStream,
        string? voiceId = null,
        int rate = 0,
        int volume = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                using var synth = new SpeechSynthesizer();
                if (!string.IsNullOrEmpty(voiceId))
                {
                    try { synth.SelectVoice(voiceId); } catch { }
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
        string? voiceId = null,
        int rate = 0,
        int volume = 100,
        CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>();

        lock (_lock)
        {
            if (_synthesizer == null) InitializeSynthesizer();

            if (!string.IsNullOrEmpty(voiceId))
            {
                try { _synthesizer!.SelectVoice(voiceId); } catch { }
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
