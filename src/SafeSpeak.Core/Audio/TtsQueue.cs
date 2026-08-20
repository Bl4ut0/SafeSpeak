using System.Collections.Concurrent;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Audio;

public sealed class TtsQueueStateChangedEventArgs : EventArgs
{
    public bool IsArmed { get; init; }
    public bool IsAutoPlay { get; init; }
    public bool IsPaused { get; init; }
    public bool IsSpeaking { get; init; }
    public int QueueCount { get; init; }
}

/// <summary>
/// Thread-safe TTS playback queue with arming safety, pause/resume, and instant panic cancellation.
/// </summary>
public sealed class TtsQueue : IAsyncDisposable
{
    private readonly ITtsEngine _ttsEngine;
    private readonly IAudioRouter _audioRouter;
    private readonly IAudioRouter? _privateAudioRouter;
    private readonly ConcurrentQueue<ModerationDecision> _queue = new();
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly int _capacity;
    private int _queuedCount;

    private bool _isArmed = false;
    private bool _isAutoPlay = false;
    private bool _isPaused = false;
    private bool _isSpeaking = false;
    private CancellationTokenSource? _activePlaybackCts;
    private readonly CancellationTokenSource _queueLoopCts = new();
    private Task? _playbackLoopTask;

    public bool IsArmed { get { lock (_stateLock) return _isArmed; } }
    public bool IsAutoPlay { get { lock (_stateLock) return _isAutoPlay; } }
    public bool IsPaused { get { lock (_stateLock) return _isPaused; } }
    public bool IsSpeaking { get { lock (_stateLock) return _isSpeaking; } }
    public int Count => Volatile.Read(ref _queuedCount);
    public int Capacity => _capacity;

    public event EventHandler<TtsQueueStateChangedEventArgs>? StateChanged;
    public event EventHandler<ModerationDecision>? PlaybackStarted;
    public event EventHandler<ModerationDecision>? PlaybackFinished;

    public string? SelectedVoice { get; set; }
    public int SpeechRate { get; set; } = 0;
    public int SpeechVolume { get; set; } = 100;
    public bool BroadcastOutputEnabled { get; set; } = true;
    public bool PrivateMonitorEnabled { get; set; }
    public bool MirrorToPrivateMonitor { get; set; } = true;

    public TtsQueue(ITtsEngine ttsEngine, IAudioRouter audioRouter, int capacity = 50, IAudioRouter? privateAudioRouter = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _ttsEngine = ttsEngine;
        _audioRouter = audioRouter;
        _privateAudioRouter = privateAudioRouter;
        _capacity = capacity;
        _playbackLoopTask = Task.Run(ProcessQueueLoopAsync);
    }

    public bool Enqueue(ModerationDecision decision)
    {
        if (decision == null || !decision.Passed || string.IsNullOrWhiteSpace(decision.SpokenText))
        {
            return false;
        }

        lock (_stateLock)
        {
            if (_queuedCount >= _capacity)
            {
                return false;
            }

            _queue.Enqueue(decision);
            _queuedCount++;
        }
        _signal.Release();
        NotifyStateChanged();
        return true;
    }

    public void SetArmed(bool armed)
    {
        lock (_stateLock)
        {
            _isArmed = armed;
        }
        WakePlaybackLoopIfReady();
        NotifyStateChanged();
    }

    public void SetAutoPlay(bool autoPlay)
    {
        lock (_stateLock)
        {
            _isAutoPlay = autoPlay;
        }
        WakePlaybackLoopIfReady();
        NotifyStateChanged();
    }

    public void SetPaused(bool paused)
    {
        lock (_stateLock)
        {
            _isPaused = paused;
        }
        WakePlaybackLoopIfReady();
        NotifyStateChanged();
    }

    public void SkipCurrent()
    {
        lock (_stateLock)
        {
            _activePlaybackCts?.Cancel();
            _ttsEngine.Stop();
            _audioRouter.Stop();
            _privateAudioRouter?.Stop();
        }
    }

    public void Clear()
    {
        while (TryDequeue(out _)) { }
        NotifyStateChanged();
    }

    /// <summary>
    /// Emergency Panic Stop: Instantly stops all current speech and purges the entire queue in &lt; 5ms.
    /// </summary>
    public void EmergencyPanicFlush()
    {
        lock (_stateLock)
        {
            _isArmed = false; // Disarm immediately for safety
            _isAutoPlay = false;
            _activePlaybackCts?.Cancel();
            _ttsEngine.Stop();
            _audioRouter.Stop();
            _privateAudioRouter?.Stop();
        }

        Clear();
    }

    private async Task ProcessQueueLoopAsync()
    {
        var token = _queueLoopCts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(token);

                if (!IsArmed || !IsAutoPlay || IsPaused)
                {
                    // If disarmed, paused, or manual-only, wait until conditions change
                    continue;
                }

                if (TryDequeue(out var decision))
                {
                    await PlayDecisionAsync(decision, token);
                    WakePlaybackLoopIfReady();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Continue loop on transient error
            }
        }
    }

    public async Task PlayNextManualAsync()
    {
        if (TryDequeue(out var decision))
        {
            await PlayDecisionAsync(decision, CancellationToken.None);
        }
    }

    private async Task PlayDecisionAsync(ModerationDecision decision, CancellationToken cancellationToken)
    {
        CancellationTokenSource playbackCts;
        lock (_stateLock)
        {
            _isSpeaking = true;
            _activePlaybackCts?.Dispose();
            _activePlaybackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            playbackCts = _activePlaybackCts;
        }

        NotifyStateChanged();
        PlaybackStarted?.Invoke(this, decision);

        try
        {
            using var waveStream = new MemoryStream();
            await _ttsEngine.SynthesizeToWaveStreamAsync(
                decision.SpokenText,
                waveStream,
                SelectedVoice,
                SpeechRate,
                SpeechVolume,
                playbackCts.Token
            );

            byte[] waveBytes = waveStream.ToArray();
            var playbackTasks = new List<Task>(2);
            if (BroadcastOutputEnabled)
            {
                playbackTasks.Add(_audioRouter.PlayWaveStreamAsync(
                    new MemoryStream(waveBytes, writable: false), SpeechVolume / 100.0f, playbackCts.Token));
            }
            if (PrivateMonitorEnabled && MirrorToPrivateMonitor && _privateAudioRouter is not null &&
                (!BroadcastOutputEnabled || _privateAudioRouter.SelectedEndpointId != _audioRouter.SelectedEndpointId))
            {
                playbackTasks.Add(_privateAudioRouter.PlayWaveStreamAsync(
                    new MemoryStream(waveBytes, writable: false), SpeechVolume / 100.0f, playbackCts.Token));
            }
            if (playbackTasks.Count > 0) await Task.WhenAll(playbackTasks);
        }
        catch (OperationCanceledException)
        {
            // Expected on skip/panic
        }
        catch
        {
            // Fail safely without crashing
        }
        finally
        {
            lock (_stateLock)
            {
                _isSpeaking = false;
            }
            NotifyStateChanged();
            PlaybackFinished?.Invoke(this, decision);
        }
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, new TtsQueueStateChangedEventArgs
        {
            IsArmed = IsArmed,
            IsAutoPlay = IsAutoPlay,
            IsPaused = IsPaused,
            IsSpeaking = IsSpeaking,
            QueueCount = Count
        });
    }

    private bool TryDequeue(out ModerationDecision decision)
    {
        lock (_stateLock)
        {
            if (!_queue.TryDequeue(out ModerationDecision? dequeued) || dequeued is null)
            {
                decision = null!;
                return false;
            }

            decision = dequeued;
            _queuedCount--;
            return true;
        }
    }

    private void WakePlaybackLoopIfReady()
    {
        if (Count == 0 || !IsArmed || !IsAutoPlay || IsPaused || _queueLoopCts.IsCancellationRequested)
        {
            return;
        }

        _signal.Release();
    }

    public async ValueTask DisposeAsync()
    {
        _queueLoopCts.Cancel();
        _activePlaybackCts?.Cancel();
        _ttsEngine.Stop();
        _audioRouter.Stop();
        _privateAudioRouter?.Stop();

        if (_playbackLoopTask != null)
        {
            try { await _playbackLoopTask; } catch { }
        }

        _signal.Dispose();
        _queueLoopCts.Dispose();
        _activePlaybackCts?.Dispose();
    }
}
