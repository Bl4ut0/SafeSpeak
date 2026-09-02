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
        int Volume,
        TaskCompletionSource Completion);

    private readonly ITtsEngine _ttsEngine;
    private readonly IAudioRouter _audioRouter;
    private readonly Channel<SpeechRequest> _requests;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _playbackLock = new();
    private readonly Task _worker;
    private CancellationTokenSource? _activeRequestCts;
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
            FullMode = BoundedChannelFullMode.Wait
        });
        _worker = Task.Run(ProcessRequestsAsync);
    }

    public void Speak(string text, bool interrupt = false)
    {
        _ = SpeakAsync(text, interrupt);
    }

    public Task SpeakAsync(string text, bool interrupt = false)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;

        long version = Interlocked.Increment(ref _latestVersion);
        if (interrupt) StopPlayback();

        while (_requests.Reader.TryRead(out SpeechRequest? supersededRequest))
        {
            supersededRequest.Completion.TrySetCanceled();
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new SpeechRequest(
            version,
            text,
            VoiceId,
            Math.Clamp(Rate, -5, 5),
            Math.Clamp(Volume, 0, 100),
            completion);
        if (!_requests.Writer.TryWrite(request))
        {
            completion.TrySetException(
                new InvalidOperationException("The voice preview queue is unavailable."));
        }

        return completion.Task;
    }

    public void Stop()
    {
        Interlocked.Increment(ref _latestVersion);
        while (_requests.Reader.TryRead(out SpeechRequest? request))
        {
            request.Completion.TrySetCanceled();
        }
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
                    var requestCts = CancellationTokenSource.CreateLinkedTokenSource(
                        _lifetimeCts.Token);
                    lock (_playbackLock)
                    {
                        _activeRequestCts?.Dispose();
                        _activeRequestCts = requestCts;
                    }

                    using var waveStream = new MemoryStream();
                    await _ttsEngine.SynthesizeToWaveStreamAsync(
                        request.Text,
                        waveStream,
                        request.VoiceId,
                        request.Rate,
                        100,
                        requestCts.Token);

                    if (request.Version != Volatile.Read(ref _latestVersion))
                    {
                        request.Completion.TrySetCanceled();
                        continue;
                    }

                    await _audioRouter.PlayWaveStreamAsync(
                        new MemoryStream(waveStream.ToArray(), writable: false),
                        request.Volume / 100.0f,
                        requestCts.Token);
                    request.Completion.TrySetResult();
                }
                catch (OperationCanceledException)
                {
                    request.Completion.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    request.Completion.TrySetException(ex);
                }
                finally
                {
                    lock (_playbackLock)
                    {
                        _activeRequestCts?.Dispose();
                        _activeRequestCts = null;
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
            _activeRequestCts?.Cancel();
            _audioRouter.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _requests.Writer.TryComplete();
        Stop();
        _lifetimeCts.Cancel();

        try { await _worker; }
        catch (OperationCanceledException) { }

        lock (_playbackLock)
        {
            _activeRequestCts?.Dispose();
            _activeRequestCts = null;
        }
        _lifetimeCts.Dispose();
    }
}
