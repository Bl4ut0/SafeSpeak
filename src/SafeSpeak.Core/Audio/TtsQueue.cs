using System.Collections.Concurrent;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Audio;

public enum TtsPlaybackMode
{
    Disarmed = 0,
    Automatic = 1,
    Paused = 2,
    Manual = 3
}

public sealed class TtsQueueStateChangedEventArgs : EventArgs
{
    public required TtsPlaybackMode Mode { get; init; }
    public bool IsArmed { get; init; }
    public bool IsAutoPlay { get; init; }
    public bool IsPaused { get; init; }
    public bool IsSpeaking { get; init; }
    public int QueueCount { get; init; }
}

/// <summary>
/// Thread-safe TTS playback queue with one explicit playback mode, bounded
/// pending work, serialized speech, and immediate cancellation controls.
/// </summary>
public sealed class TtsQueue : IAsyncDisposable
{
    private readonly ITtsEngine _ttsEngine;
    private readonly IAudioRouter _audioRouter;
    private readonly ConcurrentQueue<ModerationDecision> _queue = new();
    private readonly ConcurrentQueue<ModerationDecision> _pauseBypassQueue = new();
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private readonly int _capacity;
    private readonly CancellationTokenSource _queueLoopCts = new();

    private int _queuedCount;
    private TtsPlaybackMode _mode = TtsPlaybackMode.Disarmed;
    private bool _isSpeaking;
    private bool _disposed;
    private CancellationTokenSource? _activePlaybackCts;
    private readonly Task _playbackLoopTask;

    public TtsPlaybackMode Mode
    {
        get
        {
            lock (_stateLock)
            {
                return _mode;
            }
        }
    }

    public bool IsArmed => Mode != TtsPlaybackMode.Disarmed;
    public bool IsAutoPlay => Mode == TtsPlaybackMode.Automatic;
    public bool IsPaused => Mode == TtsPlaybackMode.Paused;

    public bool IsSpeaking
    {
        get
        {
            lock (_stateLock)
            {
                return _isSpeaking;
            }
        }
    }

    public int Count => Volatile.Read(ref _queuedCount);
    public int Capacity => _capacity;

    public event EventHandler<TtsQueueStateChangedEventArgs>? StateChanged;
    public event EventHandler<ModerationDecision>? PlaybackStarted;
    public event EventHandler<ModerationDecision>? PlaybackFinished;

    public string? SelectedVoice { get; set; }
    public int SpeechRate { get; set; }
    public int SpeechVolume { get; set; } = 100;
    public bool BroadcastOutputEnabled { get; set; } = true;

    public TtsQueue(
        ITtsEngine ttsEngine,
        IAudioRouter audioRouter,
        int capacity = 50)
    {
        ArgumentNullException.ThrowIfNull(ttsEngine);
        ArgumentNullException.ThrowIfNull(audioRouter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _ttsEngine = ttsEngine;
        _audioRouter = audioRouter;
        _capacity = capacity;
        _playbackLoopTask = Task.Run(ProcessQueueLoopAsync);
    }

    /// <summary>
    /// Adds an approved decision only while SafeSpeak is armed. Content received
    /// while disarmed is deliberately discarded so it cannot speak after re-arm.
    /// </summary>
    public bool Enqueue(ModerationDecision decision, bool bypassPause = false)
    {
        if (decision is null || !decision.Passed || string.IsNullOrWhiteSpace(decision.SpokenText))
        {
            return false;
        }

        lock (_stateLock)
        {
            if (_disposed || _mode == TtsPlaybackMode.Disarmed || _queuedCount >= _capacity)
            {
                return false;
            }

            if (bypassPause && _mode == TtsPlaybackMode.Paused)
            {
                _pauseBypassQueue.Enqueue(decision);
            }
            else
            {
                _queue.Enqueue(decision);
            }
            _queuedCount++;
        }

        ReleaseSignal();
        NotifyStateChanged();
        return true;
    }

    /// <summary>
    /// Arms SafeSpeak in its normal automatic speaking mode.
    /// </summary>
    public void ArmAutomatic()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _mode = TtsPlaybackMode.Automatic;
        }

        WakePlaybackLoopIfReady();
        NotifyStateChanged();
    }

    public void SetArmed(bool armed)
    {
        if (armed)
        {
            ArmAutomatic();
        }
        else
        {
            Disarm();
        }
    }

    /// <summary>
    /// Disarms SafeSpeak, stops current speech, and discards every pending item.
    /// </summary>
    public void Disarm()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _mode = TtsPlaybackMode.Disarmed;
            _activePlaybackCts?.Cancel();
            ClearQueueLocked();
        }

        StopOutputs();
        NotifyStateChanged();
    }

    /// <summary>
    /// Compatibility entry point for existing callers. True selects Automatic;
    /// false selects Manual, but neither value arms a disarmed queue.
    /// </summary>
    public void SetAutoPlay(bool autoPlay)
    {
        lock (_stateLock)
        {
            if (_disposed || _mode == TtsPlaybackMode.Disarmed)
            {
                return;
            }

            _mode = autoPlay ? TtsPlaybackMode.Automatic : TtsPlaybackMode.Manual;
        }

        WakePlaybackLoopIfReady();
        NotifyStateChanged();
    }

    /// <summary>
    /// Pausing lets the current item finish and prevents automatic advancement.
    /// Resuming a paused queue returns it to Automatic mode.
    /// </summary>
    public void SetPaused(bool paused)
    {
        lock (_stateLock)
        {
            if (_disposed || _mode == TtsPlaybackMode.Disarmed)
            {
                return;
            }

            if (paused)
            {
                _mode = TtsPlaybackMode.Paused;
            }
            else if (_mode == TtsPlaybackMode.Paused)
            {
                _mode = TtsPlaybackMode.Automatic;
            }
        }

        WakePlaybackLoopIfReady();
        NotifyStateChanged();
    }

    public void UseManualAdvance()
    {
        SetAutoPlay(false);
    }

    public void ResumeAutomatic()
    {
        SetAutoPlay(true);
    }

    /// <summary>
    /// Cancels only the item currently being synthesized or played.
    /// </summary>
    public void StopCurrentSpeech()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _activePlaybackCts?.Cancel();
        }

        StopOutputs();
    }

    /// <summary>
    /// Clears pending messages without interrupting current speech.
    /// </summary>
    public void ClearQueue()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            ClearQueueLocked();
        }

        NotifyStateChanged();
    }

    /// <summary>
    /// Immediately stops speech, clears pending messages, and disarms SafeSpeak.
    /// Re-arming is required before any new message can enter or play.
    /// </summary>
    public void EmergencyStop()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _mode = TtsPlaybackMode.Disarmed;
            _activePlaybackCts?.Cancel();
            ClearQueueLocked();
        }

        StopOutputs();
        NotifyStateChanged();
    }

    public async Task<bool> PlayNextManualAsync(CancellationToken cancellationToken = default)
    {
        return await TryPlayNextAsync(
            requiredMode: TtsPlaybackMode.Manual,
            cancellationToken);
    }

    private async Task ProcessQueueLoopAsync()
    {
        CancellationToken token = _queueLoopCts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await _signal.WaitAsync(token);

                while (await TryPlayNextPauseBypassAsync(token))
                {
                    // Priority event announcements may drain without changing
                    // the user's paused playback mode.
                }

                while (await TryPlayNextAsync(TtsPlaybackMode.Automatic, token))
                {
                    // Drain only while the queue remains in Automatic mode.
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task<bool> TryPlayNextAsync(
        TtsPlaybackMode requiredMode,
        CancellationToken cancellationToken)
    {
        return await TryPlayNextAsync(
            requiredMode,
            _queue,
            cancellationToken);
    }

    private async Task<bool> TryPlayNextPauseBypassAsync(
        CancellationToken cancellationToken)
    {
        return await TryPlayNextAsync(
            requiredMode: null,
            _pauseBypassQueue,
            cancellationToken);
    }

    private async Task<bool> TryPlayNextAsync(
        TtsPlaybackMode? requiredMode,
        ConcurrentQueue<ModerationDecision> sourceQueue,
        CancellationToken cancellationToken)
    {
        try
        {
            await _playbackGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        ModerationDecision? decision = null;
        CancellationTokenSource? playbackCts = null;

        try
        {
            lock (_stateLock)
            {
                if (_disposed ||
                    _mode == TtsPlaybackMode.Disarmed ||
                    (requiredMode.HasValue && _mode != requiredMode.Value) ||
                    !sourceQueue.TryDequeue(out decision) ||
                    decision is null)
                {
                    return false;
                }

                _queuedCount--;
                _isSpeaking = true;
                _activePlaybackCts?.Dispose();
                playbackCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _queueLoopCts.Token);
                _activePlaybackCts = playbackCts;
            }

            NotifyStateChanged();
            RaisePlaybackEvent(PlaybackStarted, decision);
            await SynthesizeAndPlayAsync(decision, playbackCts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return decision is not null;
        }
        catch
        {
            // A single synthesis or router failure must not terminate the queue.
            return decision is not null;
        }
        finally
        {
            if (decision is not null)
            {
                lock (_stateLock)
                {
                    _isSpeaking = false;
                    if (ReferenceEquals(_activePlaybackCts, playbackCts))
                    {
                        _activePlaybackCts = null;
                    }
                }

                playbackCts?.Dispose();
                NotifyStateChanged();
                RaisePlaybackEvent(PlaybackFinished, decision);
            }

            _playbackGate.Release();

            WakePlaybackLoopIfReady();
        }
    }

    private async Task SynthesizeAndPlayAsync(
        ModerationDecision decision,
        CancellationToken cancellationToken)
    {
        using var waveStream = new MemoryStream();
        await _ttsEngine.SynthesizeToWaveStreamAsync(
            decision.SpokenText,
            waveStream,
            SelectedVoice,
            SpeechRate,
            SpeechVolume,
            cancellationToken);

        if (BroadcastOutputEnabled)
        {
            await _audioRouter.PlayWaveStreamAsync(
                new MemoryStream(waveStream.ToArray(), writable: false),
                SpeechVolume / 100.0f,
                cancellationToken);
        }
    }

    private void ClearQueueLocked()
    {
        while (_queue.TryDequeue(out _))
        {
        }
        while (_pauseBypassQueue.TryDequeue(out _))
        {
        }

        _queuedCount = 0;
    }

    private void WakePlaybackLoopIfReady()
    {
        bool ready;
        lock (_stateLock)
        {
            ready = !_disposed &&
                _mode != TtsPlaybackMode.Disarmed &&
                ((!_pauseBypassQueue.IsEmpty) ||
                 (_mode == TtsPlaybackMode.Automatic && !_queue.IsEmpty)) &&
                !_queueLoopCts.IsCancellationRequested;
        }

        if (ready)
        {
            ReleaseSignal();
        }
    }

    private void ReleaseSignal()
    {
        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown won the race.
        }
    }

    private void StopOutputs()
    {
        TryStop(_ttsEngine.Stop);
        TryStop(_audioRouter.Stop);
    }

    private static void TryStop(Action stop)
    {
        try
        {
            stop();
        }
        catch
        {
            // Stop paths are best effort and must never block emergency handling.
        }
    }

    private void NotifyStateChanged()
    {
        TtsQueueStateChangedEventArgs snapshot;
        lock (_stateLock)
        {
            snapshot = new TtsQueueStateChangedEventArgs
            {
                Mode = _mode,
                IsArmed = _mode != TtsPlaybackMode.Disarmed,
                IsAutoPlay = _mode == TtsPlaybackMode.Automatic,
                IsPaused = _mode == TtsPlaybackMode.Paused,
                IsSpeaking = _isSpeaking,
                QueueCount = _queuedCount
            };
        }

        StateChanged?.Invoke(this, snapshot);
    }

    private void RaisePlaybackEvent(
        EventHandler<ModerationDecision>? handler,
        ModerationDecision decision)
    {
        try
        {
            handler?.Invoke(this, decision);
        }
        catch
        {
            // UI observers cannot be allowed to stop safety-critical queue work.
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _mode = TtsPlaybackMode.Disarmed;
            _activePlaybackCts?.Cancel();
            ClearQueueLocked();
        }

        _queueLoopCts.Cancel();
        StopOutputs();

        try
        {
            await _playbackLoopTask;
        }
        catch
        {
            // Shutdown is best effort.
        }

        await _playbackGate.WaitAsync();
        _playbackGate.Release();

        lock (_stateLock)
        {
            _activePlaybackCts?.Dispose();
            _activePlaybackCts = null;
            _isSpeaking = false;
        }

        _signal.Dispose();
        _playbackGate.Dispose();
        _queueLoopCts.Dispose();
    }
}
