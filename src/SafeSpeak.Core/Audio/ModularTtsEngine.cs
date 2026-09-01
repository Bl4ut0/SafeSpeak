using System.Speech.Synthesis;

namespace SafeSpeak.Core.Audio;

/// <summary>
/// Unified TTS engine routing each advertised voice to its real Windows or Kokoro provider.
/// </summary>
public sealed class ModularTtsEngine : ITtsEngine
{
    private SpeechSynthesizer? _synthesizer;
    private readonly KokoroModelManager _kokoroManager;
    private readonly Func<IWaveSpeechSynthesizer> _waveSynthesizerFactory;
    private readonly HashSet<WaveSpeechSynthesisOperation> _activeWaveSyntheses = [];
    private readonly object _lock = new();
    private bool _disposed;

    public KokoroModelManager KokoroManager => _kokoroManager;

    public ModularTtsEngine(KokoroModelManager? kokoroManager = null)
        : this(
            kokoroManager,
            static () => new SystemSpeechWaveSynthesizer(),
            initializeDirectSynthesizer: true)
    {
    }

    internal ModularTtsEngine(
        KokoroModelManager? kokoroManager,
        Func<IWaveSpeechSynthesizer> waveSynthesizerFactory,
        bool initializeDirectSynthesizer = false)
    {
        _kokoroManager = kokoroManager ?? new KokoroModelManager();
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

            // Windows SAPI voices are always available without a separate model download.
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

                        string prefix = isOneCore ? "Natural" : "System";
                        string provider = isOneCore ? "Windows OneCore Natural" : "Windows Desktop SAPI";

                        list.Add(new VoiceInfo(
                            info.Name,
                            $"{prefix} — {info.Name}",
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
