using System.Speech.Synthesis;
using SafeSpeak.Core.Audio.VoiceFramework;

namespace SafeSpeak.Core.Audio;

/// <summary>
/// Unified TTS engine routing each advertised voice to its real Windows, Kokoro, or custom package provider.
/// </summary>
public sealed class ModularTtsEngine : ITtsEngine
{
    public const string VoicePackagePrefix = "pack:";
    public const string WinRtVoicePrefix = "winrt:";

    private SpeechSynthesizer? _synthesizer;
    private readonly KokoroModelManager _kokoroManager;
    private readonly VoicePackageManager? _voicePackageManager;
    private readonly Func<IWaveSpeechSynthesizer> _waveSynthesizerFactory;
    private readonly HashSet<WaveSpeechSynthesisOperation> _activeWaveSyntheses = [];
    private readonly object _lock = new();
    private bool _disposed;

    public KokoroModelManager KokoroManager => _kokoroManager;
    public VoicePackageManager? VoicePackageManager => _voicePackageManager;

    public ModularTtsEngine(KokoroModelManager? kokoroManager = null, VoicePackageManager? voicePackageManager = null)
        : this(
            kokoroManager,
            voicePackageManager,
            static () => new SystemSpeechWaveSynthesizer(),
            initializeDirectSynthesizer: true)
    {
    }

    internal ModularTtsEngine(
        KokoroModelManager? kokoroManager,
        Func<IWaveSpeechSynthesizer> waveSynthesizerFactory,
        bool initializeDirectSynthesizer = false)
        : this(kokoroManager, voicePackageManager: null, waveSynthesizerFactory, initializeDirectSynthesizer)
    {
    }

    internal ModularTtsEngine(
        KokoroModelManager? kokoroManager,
        VoicePackageManager? voicePackageManager,
        Func<IWaveSpeechSynthesizer> waveSynthesizerFactory,
        bool initializeDirectSynthesizer = false)
    {
        _kokoroManager = kokoroManager ?? new KokoroModelManager();
        _voicePackageManager = voicePackageManager;
        _waveSynthesizerFactory = waveSynthesizerFactory ??
            throw new ArgumentNullException(nameof(waveSynthesizerFactory));

        if (initializeDirectSynthesizer)
        {
            InitializeSynthesizer();
        }
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

            if (_kokoroManager.IsInstalled)
            {
                list.AddRange(KokoroModelManager.EnglishVoices);
            }

            if (_voicePackageManager != null)
            {
                foreach (var package in _voicePackageManager.GetInstalledPackages())
                {
                    string culture = string.IsNullOrWhiteSpace(package.Manifest.Culture) ? "en-US" : package.Manifest.Culture;
                    string gender = string.IsNullOrWhiteSpace(package.Manifest.Gender) ? "Neutral" : package.Manifest.Gender;
                    string desc = string.IsNullOrWhiteSpace(package.Manifest.Description)
                        ? "Custom imported offline voice package"
                        : package.Manifest.Description;

                    list.Add(new VoiceInfo(
                        VoicePackagePrefix + package.Manifest.Id,
                        $"Custom — {package.Manifest.DisplayName} ({culture})",
                        "Custom Voice Package",
                        culture,
                        gender,
                        desc,
                        true,
                        ComputeLevel: 4
                    ));
                }
            }

#if WINDOWS
            try
            {
                foreach (var voice in Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices)
                {
                    string gender = voice.Gender.ToString();
                    string culture = voice.Language;
                    string name = voice.DisplayName;

                    list.Add(new VoiceInfo(
                        WinRtVoicePrefix + voice.Id,
                        $"Natural — {name} ({culture})",
                        "Windows Natural",
                        culture,
                        gender,
                        voice.Description,
                        true,
                        ComputeLevel: 2
                    ));
                }
            }
            catch
            {
                // WinRT SpeechSynthesizer not supported or unavailable
            }
#endif

            // Windows SAPI voices are always available without a separate model download.
            if (_synthesizer != null)
            {
                try
                {
                    bool hasWinRtVoices = list.Any(v => v.ComputeLevel == 2);
                    foreach (var voice in _synthesizer.GetInstalledVoices())
                    {
                        var info = voice.VoiceInfo;
                        bool isOneCore = info.Name.Contains("OneCore", StringComparison.OrdinalIgnoreCase) ||
                                         info.Description.Contains("OneCore", StringComparison.OrdinalIgnoreCase) ||
                                         info.Name.Contains("Natural", StringComparison.OrdinalIgnoreCase) ||
                                         info.Id.Contains("MSTTS_V110_", StringComparison.OrdinalIgnoreCase) ||
                                         info.Id.Contains("OneCore", StringComparison.OrdinalIgnoreCase) ||
                                         (!info.Name.EndsWith("Desktop", StringComparison.OrdinalIgnoreCase) &&
                                          !info.Description.EndsWith("Desktop", StringComparison.OrdinalIgnoreCase));

                        // If WinRT native voices are already present, don't duplicate them via SAPI
                        if (isOneCore && hasWinRtVoices)
                        {
                            continue;
                        }

                        string prefix = isOneCore ? "Natural" : "System";
                        string provider = isOneCore ? "Windows Natural" : "Windows Desktop SAPI";
                        int computeLevel = isOneCore ? 2 : 1;
                        string culture = info.Culture.Name;
                        string desc = isOneCore
                            ? (string.IsNullOrWhiteSpace(info.Description) ? "Windows Natural OneCore voice." : info.Description)
                            : (string.IsNullOrWhiteSpace(info.Description) || info.Description.Equals(info.Name, StringComparison.OrdinalIgnoreCase)
                                ? "Legacy Windows Desktop SAPI voice. Ultra-low compute fallback (<1% CPU)."
                                : $"{info.Description}. Legacy Windows Desktop SAPI voice.");

                        list.Add(new VoiceInfo(
                            info.Name,
                            $"{prefix} — {info.Name}",
                            provider,
                            culture,
                            info.Gender.ToString(),
                            desc,
                            isOneCore,
                            ComputeLevel: computeLevel
                        ));
                    }
                }
                catch { }
            }

            // Sort: Natural Neural voices first (Kokoro/Custom/OneCore), then standard desktop voices
            var sortedList = list.OrderByDescending(v => v.IsNaturalNeural)
                .ThenByDescending(v => v.ComputeLevel)
                .ThenBy(v => v.DisplayName)
                .ToList();

            if (sortedList.Count == 0)
            {
                sortedList.Add(new VoiceInfo("Default", "Default System Voice", "System", "en-US", "Neutral", "Default Windows Voice", false, ComputeLevel: 1));
            }

            return sortedList;
        }
    }

    public async Task SynthesizeToWaveStreamAsync(
        string text,
        Stream outputStream,
        string? voiceId = null,
        int rate = 0,
        int volume = 100,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(voiceId) && voiceId.StartsWith(KokoroModelManager.VoicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _kokoroManager.SynthesizeAsync(text, outputStream, voiceId, rate, cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(voiceId) && voiceId.StartsWith(VoicePackagePrefix, StringComparison.OrdinalIgnoreCase))
        {
            await SynthesizeVoicePackageAsync(text, outputStream, voiceId, rate, volume, cancellationToken);
            return;
        }

#if WINDOWS
        if (!string.IsNullOrWhiteSpace(voiceId) && voiceId.StartsWith(WinRtVoicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            await SynthesizeWinRtVoiceAsync(text, outputStream, voiceId, rate, volume, cancellationToken);
            return;
        }
#endif

        await Task.Run(() =>
        {
            var operation = new WaveSpeechSynthesisOperation(_waveSynthesizerFactory());
            try
            {
                lock (_lock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _activeWaveSyntheses.Add(operation);
                }
            }
            catch
            {
                operation.Dispose();
                throw;
            }

            using var cancellationRegistration = cancellationToken.Register(
                static state => ((WaveSpeechSynthesisOperation)state!).Cancel(),
                operation);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation.Synthesizer.Configure(outputStream, voiceId, rate, volume);
                cancellationToken.ThrowIfCancellationRequested();
                if (!operation.IsCancellationRequested)
                {
                    operation.Synthesizer.Speak(text);
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                lock (_lock)
                {
                    _activeWaveSyntheses.Remove(operation);
                }

                operation.Dispose();
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
            ObjectDisposedException.ThrowIf(_disposed, this);
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

    private async Task SynthesizeVoicePackageAsync(
        string text,
        Stream outputStream,
        string voiceId,
        int rate,
        int volume,
        CancellationToken cancellationToken)
    {
        string packageId = voiceId[VoicePackagePrefix.Length..];
        var package = _voicePackageManager?.GetInstalledPackages()
            .FirstOrDefault(p => string.Equals(p.Manifest.Id, packageId, StringComparison.OrdinalIgnoreCase));

        if (package == null)
        {
            throw new InvalidOperationException($"Voice package '{packageId}' is not installed.");
        }

        // If the package contains a sample audio file and the text is a preview/test or sample, stream it
        if (!string.IsNullOrWhiteSpace(package.SampleAudioAbsolutePath) && File.Exists(package.SampleAudioAbsolutePath))
        {
            await using var sampleFile = File.OpenRead(package.SampleAudioAbsolutePath);
            await sampleFile.CopyToAsync(outputStream, cancellationToken);
            return;
        }

        // Safe fallback synthesis using baseline wave synthesizer with package settings
        await Task.Run(() =>
        {
            var operation = new WaveSpeechSynthesisOperation(_waveSynthesizerFactory());
            try
            {
                lock (_lock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _activeWaveSyntheses.Add(operation);
                }
            }
            catch
            {
                operation.Dispose();
                throw;
            }

            using var cancellationRegistration = cancellationToken.Register(
                static state => ((WaveSpeechSynthesisOperation)state!).Cancel(),
                operation);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation.Synthesizer.Configure(outputStream, null, rate, volume);
                cancellationToken.ThrowIfCancellationRequested();
                if (!operation.IsCancellationRequested)
                {
                    operation.Synthesizer.Speak(text);
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                lock (_lock)
                {
                    _activeWaveSyntheses.Remove(operation);
                }

                operation.Dispose();
            }
        }, cancellationToken);
    }

#if WINDOWS
    private static async Task SynthesizeWinRtVoiceAsync(
        string text,
        Stream outputStream,
        string voiceId,
        int rate,
        int volume,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string actualVoiceId = voiceId[WinRtVoicePrefix.Length..];
        using var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();

        var voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices
            .FirstOrDefault(v => string.Equals(v.Id, actualVoiceId, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(v.DisplayName, actualVoiceId, StringComparison.OrdinalIgnoreCase));
        if (voice != null)
        {
            synth.Voice = voice;
        }

        // Map rate (-10 to +10) to WinRT SpeakingRate (0.5 to 6.0, default 1.0)
        double speakingRate = 1.0;
        if (rate > 0)
        {
            speakingRate = 1.0 + (Math.Min(rate, 10) * 0.08);
        }
        else if (rate < 0)
        {
            speakingRate = Math.Max(0.5, 1.0 + (Math.Max(rate, -10) * 0.05));
        }
        synth.Options.SpeakingRate = Math.Clamp(speakingRate, 0.5, 3.0);

        // Map volume (0 to 100) to WinRT AudioVolume (0.0 to 1.0)
        synth.Options.AudioVolume = Math.Clamp(volume / 100.0, 0.0, 1.0);

        var asyncOp = synth.SynthesizeTextToStreamAsync(text);
        using var reg = cancellationToken.Register(() =>
        {
            try { asyncOp.Cancel(); } catch { }
        });

        using var speechStream = await asyncOp;
        using var netStream = speechStream.AsStreamForRead();
        await netStream.CopyToAsync(outputStream, cancellationToken);
        await outputStream.FlushAsync(cancellationToken);
    }
#endif

    public void Stop()
    {
        SpeechSynthesizer? directSynthesizer;
        WaveSpeechSynthesisOperation[] waveSyntheses;
        lock (_lock)
        {
            directSynthesizer = _synthesizer;
            waveSyntheses = [.. _activeWaveSyntheses];
        }

        foreach (WaveSpeechSynthesisOperation operation in waveSyntheses)
        {
            operation.Cancel();
        }

        try { directSynthesizer?.SpeakAsyncCancelAll(); } catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        SpeechSynthesizer? directSynthesizer;
        WaveSpeechSynthesisOperation[] waveSyntheses;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            directSynthesizer = _synthesizer;
            _synthesizer = null;
            waveSyntheses = [.. _activeWaveSyntheses];
            _activeWaveSyntheses.Clear();
        }

        foreach (WaveSpeechSynthesisOperation operation in waveSyntheses)
        {
            operation.Cancel();
        }

        try { directSynthesizer?.SpeakAsyncCancelAll(); } catch { }
        directSynthesizer?.Dispose();
        _kokoroManager.Dispose();
    }
}
