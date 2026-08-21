using System.Threading.Channels;
using SafeSpeak.Core.Audio;

namespace SafeSpeak.Core.Accessibility;

/// <summary>
/// Synthesizes the selected stream voice for an explicit private preview. This
/// is intentionally separate from the low-latency integrated navigation reader.
/// </summary>
public sealed class PrivateVoicePreviewOutput : IPrivateVoiceOutput, IAsyncDisposable
{
    private sealed record SpeechRequest(
        long Version,
        string Text,
        string? VoiceId,
        int Rate,
        int Volume);

    private readonly ITtsEngine _ttsEngine;
    private readonly IAudioRouter _audioRouter;
    private readonly Channel<SpeechRequest> _requests;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _playbackLock = new();
    private readonly Task _worker;
    private CancellationTokenSource? _playbackCts;
    private long _latestVersion;
    private bool _disposed;

    public string? VoiceId { get; set; }
    public int Rate { get; set; }
    public int Volume { get; set; } = 100;

    public PrivateVoicePreviewOutput(ITtsEngine ttsEngine, IAudioRouter audioRouter)
    {
        _ttsEngine = ttsEngine;
        _audioRouter = audioRouter;
        _requests = Channel.CreateBounded<SpeechRequest>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _worker = Task.Run(ProcessRequestsAsync);
    }

    public void Speak(string text, bool interrupt = false)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text)) return;

        long version = Interlocked.Increment(ref _latestVersion);
        if (interrupt) StopPlayback();

        _requests.Writer.TryWrite(new SpeechRequest(
            version,
            text,
            VoiceId,
            Math.Clamp(Rate, -5, 5),
            Math.Clamp(Volume, 0, 100)));
    }

    public void Stop()
    {
        Interlocked.Increment(ref _latestVersion);
        while (_requests.Reader.TryRead(out _)) { }
        StopPlayback();
    }

    private async Task ProcessRequestsAsync()
    {
        try
        {
            await foreach (SpeechRequest initialRequest in _requests.Reader.ReadAllAsync(_lifetimeCts.Token))
            {
                SpeechRequest request = initialRequest;
                while (_requests.Reader.TryRead(out SpeechRequest? newerRequest)) request = newerRequest;

                try
                {
                    using var waveStream = new MemoryStream();
                    await _ttsEngine.SynthesizeToWaveStreamAsync(
                        request.Text,
                        waveStream,
                        request.VoiceId,
                        request.Rate,
                        100,
                        _lifetimeCts.Token);

                    if (request.Version != Volatile.Read(ref _latestVersion)) continue;

                    var playbackCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
                    lock (_playbackLock)
                    {
                        _playbackCts?.Dispose();
                        _playbackCts = playbackCts;
                    }

                    await _audioRouter.PlayWaveStreamAsync(
                        new MemoryStream(waveStream.ToArray(), writable: false),
                        request.Volume / 100.0f,
                        playbackCts.Token);
                }
                catch (OperationCanceledException) { }
                catch
                {
                    // A failed preview must not affect navigation or stream TTS.
                }
                finally
                {
                    lock (_playbackLock)
                    {
                        _playbackCts?.Dispose();
                        _playbackCts = null;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void StopPlayback()
    {
        lock (_playbackLock)
        {
            _playbackCts?.Cancel();
            _audioRouter.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _requests.Writer.TryComplete();
        _lifetimeCts.Cancel();
        StopPlayback();

        try { await _worker; }
        catch (OperationCanceledException) { }

        lock (_playbackLock)
        {
            _playbackCts?.Dispose();
            _playbackCts = null;
        }
        _lifetimeCts.Dispose();
    }
}
