using System.Collections.Concurrent;
using System.Diagnostics;
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
    private int _capacity;
    private readonly CancellationTokenSource _queueLoopCts = new();

    private int _queuedCount;
    private TtsPlaybackMode _mode = TtsPlaybackMode.Disarmed;
    private bool _isSpeaking;
    private bool _isPlayingAudio;
    private bool _disposed;
    private CancellationTokenSource? _activePlaybackCts;
    private PrefetchOperation? _prefetch;
    private long _lastAutomaticPlaybackFinishedTimestamp;
    private readonly Task _playbackLoopTask;

    private sealed record SynthesisSettings(string? VoiceId, int Rate);

    private sealed record PrefetchOperation(
        ModerationDecision Decision,
        SynthesisSettings Settings,
        CancellationTokenSource Cancellation,
        Task<byte[]> SynthesisTask);

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
    public int Capacity => Volatile.Read(ref _capacity);

    public event EventHandler<TtsQueueStateChangedEventArgs>? StateChanged;
    public event EventHandler<ModerationDecision>? PlaybackStarted;
    public event EventHandler<ModerationDecision>? PlaybackFinished;

    public string? SelectedVoice { get; set; }
    public int SpeechRate { get; set; }
    public int SpeechVolume { get; set; } = 100;
    public int SpeechBoost { get; set; } = 0;
    public bool BroadcastOutputEnabled { get; set; } = true;
    public int InterMessageGapMilliseconds { get; set; }
    public bool AdaptiveInterMessageGap { get; set; } = true;
    public int MaxQueueAgeSeconds { get; set; } = 45;

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
    /// Changes the maximum number of pending approved items. Reducing the limit
    /// never discards existing work; new items wait until the count falls below
    /// the new limit.
    /// </summary>
    public void SetCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _capacity = capacity;
        }

        NotifyStateChanged();
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
        StartPrefetch(
            bypassPause ? _pauseBypassQueue : _queue,
            bypassPause ? null : Mode);
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
            if (requiredMode != TtsPlaybackMode.Manual)
            {
                await WaitForInterMessageGapAsync(sourceQueue, cancellationToken);
            }

            lock (_stateLock)
            {
                if (_disposed ||
                    _mode == TtsPlaybackMode.Disarmed ||
                    (requiredMode.HasValue && _mode != requiredMode.Value))
                {
                    return false;
                }

                decision = null;
                while (sourceQueue.TryDequeue(out ModerationDecision? candidate))
                {
                    _queuedCount--;
                    if (candidate is null)
                    {
                        continue;
                    }

                    if (MaxQueueAgeSeconds > 0 &&
                        DateTimeOffset.UtcNow - candidate.Message.TimestampUtc > TimeSpan.FromSeconds(MaxQueueAgeSeconds))
                    {
                        // Discard stale message to catch up to the live stream
                        continue;
                    }

                    decision = candidate;
                    _isSpeaking = true;
                    _activePlaybackCts?.Dispose();
                    playbackCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        _queueLoopCts.Token);
                    _activePlaybackCts = playbackCts;
                    break;
                }

                if (decision is null || playbackCts is null)
                {
                    return false;
                }
            }

            NotifyStateChanged();
            RaisePlaybackEvent(PlaybackStarted, decision);
            await SynthesizeAndPlayAsync(
                decision,
                sourceQueue,
                requiredMode,
                playbackCts.Token);
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
                if (requiredMode != TtsPlaybackMode.Manual)
                {
                    Volatile.Write(
                        ref _lastAutomaticPlaybackFinishedTimestamp,
                        Stopwatch.GetTimestamp());
                }
                NotifyStateChanged();
                RaisePlaybackEvent(PlaybackFinished, decision);
            }

            _playbackGate.Release();

            WakePlaybackLoopIfReady();
        }
    }

    private async Task WaitForInterMessageGapAsync(
        ConcurrentQueue<ModerationDecision> sourceQueue,
        CancellationToken cancellationToken)
    {
        long lastFinished = Volatile.Read(ref _lastAutomaticPlaybackFinishedTimestamp);
        if (lastFinished == 0 || sourceQueue.IsEmpty)
        {
            return;
        }

        int maximumGap = Math.Clamp(InterMessageGapMilliseconds, 0, 5000);
        int effectiveGap = CalculateEffectiveInterMessageGap(
            maximumGap,
            AdaptiveInterMessageGap,
            sourceQueue.Count);
        if (effectiveGap <= 0)
        {
            return;
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(lastFinished);
        TimeSpan remaining = TimeSpan.FromMilliseconds(effectiveGap) - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }
    }

    internal static int CalculateEffectiveInterMessageGap(
        int maximumGapMilliseconds,
        bool adaptive,
        int pendingMessageCount)
    {
        int gap = Math.Clamp(maximumGapMilliseconds, 0, 5000);
        if (!adaptive || pendingMessageCount <= 1)
        {
            return gap;
        }

        return pendingMessageCount switch
        {
            >= 10 => 0,
            >= 5 => gap / 4,
            _ => gap / 2
        };
    }

    private async Task SynthesizeAndPlayAsync(
        ModerationDecision decision,
        ConcurrentQueue<ModerationDecision> sourceQueue,
        TtsPlaybackMode? requiredMode,
        CancellationToken cancellationToken)
    {
        var settings = new SynthesisSettings(
            SelectedVoice,
            SpeechRate);
        byte[] waveBytes = await GetOrSynthesizeWaveAsync(
            decision,
            settings,
            cancellationToken);

        if (BroadcastOutputEnabled)
        {
            lock (_stateLock)
            {
                _isPlayingAudio = true;
            }

            try
            {
                // Prepare one queued message while the current message is
                // audibly playing. This removes per-message synthesis from the
                // gap without overlapping playback or buffering the full queue.
                StartPrefetch(sourceQueue, requiredMode);
                float baseVolume = Math.Clamp(SpeechVolume, 0, 100) / 100.0f;
                float boostMultiplier = 1.0f + (Math.Clamp(SpeechBoost, 0, 100) / 100.0f);
                float playbackVolume = baseVolume * boostMultiplier;
                await _audioRouter.PlayWaveStreamAsync(
                    new MemoryStream(waveBytes, writable: false),
                    playbackVolume,
                    cancellationToken);
            }
            finally
            {
                lock (_stateLock)
                {
                    _isPlayingAudio = false;
                }
            }
        }
    }

    private async Task<byte[]> GetOrSynthesizeWaveAsync(
        ModerationDecision decision,
        SynthesisSettings settings,
        CancellationToken cancellationToken)
    {
        PrefetchOperation? matching = null;
        PrefetchOperation? stale = null;

        lock (_stateLock)
        {
            if (_prefetch is not null &&
                ReferenceEquals(_prefetch.Decision, decision) &&
                _prefetch.Settings == settings)
            {
                matching = _prefetch;
                _prefetch = null;
            }
            else if (_prefetch is not null)
            {
                stale = _prefetch;
                _prefetch = null;
            }
        }

        CancelAndDisposeWhenComplete(stale);

        if (matching is not null)
        {
            bool disposeDirectly = true;
            try
            {
                return await matching.SynthesisTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancelAndDisposeWhenComplete(matching);
                disposeDirectly = false;
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A stop operation can cancel an in-flight preview. If this
                // item is still eligible, synthesize it again as current work.
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // A speculative failure must not discard an approved message;
                // the normal synthesis path gets one authoritative attempt.
            }
            finally
            {
                if (disposeDirectly)
                {
                    matching.Cancellation.Dispose();
                }
            }
        }

        return await SynthesizeWaveAsync(decision, settings, cancellationToken);
    }

    private async Task<byte[]> SynthesizeWaveAsync(
        ModerationDecision decision,
        SynthesisSettings settings,
        CancellationToken cancellationToken)
    {
        using var waveStream = new MemoryStream();
        await _ttsEngine.SynthesizeToWaveStreamAsync(
            decision.SpokenText,
            waveStream,
            settings.VoiceId,
            settings.Rate,
            100,
            cancellationToken);
        return waveStream.ToArray();
    }

    private void StartPrefetch(
        ConcurrentQueue<ModerationDecision> sourceQueue,
        TtsPlaybackMode? requiredMode)
    {
        PrefetchOperation? stale = null;

        lock (_stateLock)
        {
            if (_disposed ||
                !_isPlayingAudio ||
                _mode == TtsPlaybackMode.Disarmed ||
                requiredMode == TtsPlaybackMode.Manual ||
                (requiredMode.HasValue && _mode != requiredMode.Value) ||
                !sourceQueue.TryPeek(out ModerationDecision? next) ||
                next is null)
            {
                return;
            }

            if (MaxQueueAgeSeconds > 0 &&
                DateTimeOffset.UtcNow - next.Message.TimestampUtc > TimeSpan.FromSeconds(MaxQueueAgeSeconds))
            {
                return;
            }

            var settings = new SynthesisSettings(
                SelectedVoice,
                SpeechRate);
            if (_prefetch is not null &&
                ReferenceEquals(_prefetch.Decision, next) &&
                _prefetch.Settings == settings)
            {
                return;
            }

            stale = _prefetch;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _queueLoopCts.Token);
            _prefetch = new PrefetchOperation(
                next,
                settings,
                cancellation,
                SynthesizeWaveAsync(next, settings, cancellation.Token));
        }

        CancelAndDisposeWhenComplete(stale);
    }

    private static void CancelAndDisposeWhenComplete(PrefetchOperation? operation)
    {
        if (operation is null)
        {
            return;
        }

        try { operation.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
        _ = operation.SynthesisTask.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                operation.Cancellation.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ClearQueueLocked()
    {
        _queue.Clear();
        _pauseBypassQueue.Clear();
        _queuedCount = 0;
        PrefetchOperation? prefetch = _prefetch;
        _prefetch = null;
        CancelAndDisposeWhenComplete(prefetch);
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
