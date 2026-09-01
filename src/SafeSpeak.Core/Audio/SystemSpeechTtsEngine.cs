using System.Speech.Synthesis;

namespace SafeSpeak.Core.Audio;

/// <summary>
/// Native offline Windows TTS engine utilizing System.Speech.Synthesis (SAPI5 / Windows Media Speech).
/// </summary>
public sealed class SystemSpeechTtsEngine : ITtsEngine
{
    private SpeechSynthesizer? _synthesizer;
    private readonly Func<IWaveSpeechSynthesizer> _waveSynthesizerFactory;
    private readonly HashSet<WaveSpeechSynthesisOperation> _activeWaveSyntheses = [];
    private readonly object _lock = new();
    private bool _disposed;

    public SystemSpeechTtsEngine()
        : this(static () => new SystemSpeechWaveSynthesizer(), initializeDirectSynthesizer: true)
    {
    }

    internal SystemSpeechTtsEngine(
        Func<IWaveSpeechSynthesizer> waveSynthesizerFactory,
        bool initializeDirectSynthesizer = false)
    {
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
                operation.Synthesizer.Configure(outputStream, voiceName, rate, volume);
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
        string? voiceName = null,
        int rate = 0,
        int volume = 100,
        CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>();

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
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
    }
}

internal interface IWaveSpeechSynthesizer : IDisposable
{
    void Configure(Stream outputStream, string? voiceName, int rate, int volume);
    void Speak(string text);
    void Cancel();
    void ResetOutput();
}

internal sealed class SystemSpeechWaveSynthesizer : IWaveSpeechSynthesizer
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly object _gate = new();
    private bool _cancelRequested;
    private bool _disposed;

    public void Configure(Stream outputStream, string? voiceName, int rate, int volume)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cancelRequested)
            {
                return;
            }

            if (!string.IsNullOrEmpty(voiceName))
            {
                try { _synthesizer.SelectVoice(voiceName); } catch { }
            }

            _synthesizer.Rate = Math.Clamp(rate, -10, 10);
            _synthesizer.Volume = Math.Clamp(volume, 0, 100);
            _synthesizer.SetOutputToWaveStream(outputStream);
        }
    }

    public void Speak(string text)
    {
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs args)
        {
            _synthesizer.SpeakCompleted -= OnSpeakCompleted;
            completed.TrySetResult();
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cancelRequested)
            {
                return;
            }

            _synthesizer.SpeakCompleted += OnSpeakCompleted;
            try
            {
                // Queue speech before releasing the gate. Cancel either wins before this
                // point (and Speak returns) or runs afterward against this exact instance.
                _synthesizer.SpeakAsync(text);
            }
            catch
            {
                _synthesizer.SpeakCompleted -= OnSpeakCompleted;
                throw;
            }
        }

        completed.Task.GetAwaiter().GetResult();
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_disposed || _cancelRequested)
            {
                return;
            }

            _cancelRequested = true;
            _synthesizer.SpeakAsyncCancelAll();
        }
    }

    public void ResetOutput() => _synthesizer.SetOutputToNull();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _synthesizer.Dispose();
        }
    }
}

internal sealed class WaveSpeechSynthesisOperation : IDisposable
{
    private int _cancellationRequested;
    private int _disposed;

    public WaveSpeechSynthesisOperation(IWaveSpeechSynthesizer synthesizer)
    {
        Synthesizer = synthesizer;
    }

    public IWaveSpeechSynthesizer Synthesizer { get; }

    public bool IsCancellationRequested => Volatile.Read(ref _cancellationRequested) != 0;

    public void Cancel()
    {
        if (Interlocked.Exchange(ref _cancellationRequested, 1) != 0)
        {
            return;
        }

        try { Synthesizer.Cancel(); } catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try { Synthesizer.ResetOutput(); } catch { }
        Synthesizer.Dispose();
    }
}
