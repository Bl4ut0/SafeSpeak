using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public class MockTtsEngine : ITtsEngine
{
    private int _speakCount;

    public int SpeakCount => Volatile.Read(ref _speakCount);
    public bool WasStopped { get; private set; }

    public IReadOnlyList<VoiceInfo> GetAvailableVoices() =>
    [
        new("mock_voice", "Mock Voice", "Mock Provider", "en-US", "Neutral", "Mock test voice", false)
    ];

    public Task SynthesizeToWaveStreamAsync(
        string text,
        Stream outputStream,
        string? voiceName = null,
        int rate = 0,
        int volume = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _speakCount);
        outputStream.Write(new byte[44]);
        return Task.CompletedTask;
    }

    public Task SpeakDirectAsync(
        string text,
        string? voiceName = null,
        int rate = 0,
        int volume = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _speakCount);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        WasStopped = true;
    }

    public void Dispose()
    {
    }
}

public sealed class BlockingTtsEngine : ITtsEngine
{
    private readonly TaskCompletionSource _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _speakCount;

    public int SpeakCount => Volatile.Read(ref _speakCount);
    public bool WasStopped { get; private set; }
    public Task Started => _started.Task;

    public IReadOnlyList<VoiceInfo> GetAvailableVoices() => [];

    public async Task SynthesizeToWaveStreamAsync(
        string text,
        Stream outputStream,
        string? voiceName = null,
        int rate = 0,
        int volume = 100,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _speakCount);
        _started.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
        await outputStream.WriteAsync(new byte[44], cancellationToken);
    }

    public Task SpeakDirectAsync(
        string text,
        string? voiceName = null,
        int rate = 0,
        int volume = 100,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void Release()
    {
        _release.TrySetResult();
    }

    public void Stop()
    {
        WasStopped = true;
    }

    public void Dispose()
    {
    }
}

public class MockAudioRouter : IAudioRouter
{
    private int _playbackCount;

    public string? SelectedEndpointId { get; private set; }
    public bool WasStopped { get; private set; }
    public int PlaybackCount => Volatile.Read(ref _playbackCount);

    public IReadOnlyList<AudioEndpointInfo> GetOutputEndpoints() =>
    [
        new("mock_device_1", "Mock Virtual Cable", true, true)
    ];

    public void SelectEndpoint(string? endpointId)
    {
        SelectedEndpointId = endpointId;
    }

    public Task PlayWaveStreamAsync(
        Stream waveStream,
        float volume = 1,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _playbackCount);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        WasStopped = true;
    }

    public void Dispose()
    {
    }
}

public class TtsQueueTests
{
    [Fact]
    public async Task TtsQueue_StartsDisarmedByDefault()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        Assert.Equal(TtsPlaybackMode.Disarmed, queue.Mode);
        Assert.False(queue.IsArmed);
        Assert.False(queue.IsAutoPlay);
        Assert.False(queue.IsPaused);
    }

    [Fact]
    public async Task DisarmedQueue_RejectsPlaybackModeChangesAndKeepsConsistentState()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.SetAutoPlay(true);
        queue.SetAutoPlay(false);
        queue.SetPaused(true);
        queue.SetPaused(false);
        queue.UseManualAdvance();
        queue.ResumeAutomatic();

        Assert.Equal(TtsPlaybackMode.Disarmed, queue.Mode);
        Assert.False(queue.IsArmed);
        Assert.False(queue.IsAutoPlay);
        Assert.False(queue.IsPaused);

        queue.ArmAutomatic();
        Assert.Equal(TtsPlaybackMode.Automatic, queue.Mode);
        Assert.True(queue.IsArmed);
        Assert.True(queue.IsAutoPlay);
        Assert.False(queue.IsPaused);

        queue.UseManualAdvance();
        Assert.Equal(TtsPlaybackMode.Manual, queue.Mode);
        Assert.True(queue.IsArmed);
        Assert.False(queue.IsAutoPlay);
        Assert.False(queue.IsPaused);

        queue.SetPaused(true);
        Assert.Equal(TtsPlaybackMode.Paused, queue.Mode);
        Assert.True(queue.IsArmed);
        Assert.False(queue.IsAutoPlay);
        Assert.True(queue.IsPaused);
    }

    [Fact]
    public async Task EnqueueWhileDisarmed_IsDiscardedAndCannotPlayAfterRearm()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        Assert.False(queue.Enqueue(Approved("Must be discarded")));
        queue.ArmAutomatic();
        await Task.Delay(50);

        Assert.Equal(0, mockTts.SpeakCount);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task ArmAutomatic_PlaysApprovedPendingMessages()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.ArmAutomatic();
        Assert.True(queue.Enqueue(Approved("First")));
        Assert.True(queue.Enqueue(Approved("Second")));

        await WaitUntilAsync(() => mockTts.SpeakCount == 2);

        Assert.Equal(TtsPlaybackMode.Automatic, queue.Mode);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Pause_AllowsCurrentSpeechToFinishWithoutAdvancing()
    {
        var mockTts = new BlockingTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.ArmAutomatic();
        Assert.True(queue.Enqueue(Approved("First")));
        Assert.True(queue.Enqueue(Approved("Second")));
        await mockTts.Started.WaitAsync(TimeSpan.FromSeconds(2));

        queue.SetPaused(true);
        mockTts.Release();
        await WaitUntilAsync(() => !queue.IsSpeaking);
        await Task.Delay(50);

        Assert.Equal(TtsPlaybackMode.Paused, queue.Mode);
        Assert.Equal(1, mockTts.SpeakCount);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task PausedQueue_PlaysBypassEventButKeepsOrdinaryChatPending()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.ArmAutomatic();
        queue.SetPaused(true);
        Assert.True(queue.Enqueue(Approved("Ordinary chat")));
        Assert.True(queue.Enqueue(Approved("Gift announcement"), bypassPause: true));

        await WaitUntilAsync(() => mockTts.SpeakCount == 1);

        Assert.Equal(TtsPlaybackMode.Paused, queue.Mode);
        Assert.True(queue.IsPaused);
        Assert.Equal(1, queue.Count);

        queue.SetPaused(false);
        await WaitUntilAsync(() => mockTts.SpeakCount == 2);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task EmergencyStop_ClearsPendingPauseBypassEvents()
    {
        var mockTts = new BlockingTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.ArmAutomatic();
        queue.SetPaused(true);
        Assert.True(queue.Enqueue(Approved("Priority follow"), bypassPause: true));
        Assert.True(queue.Enqueue(Approved("Priority gift"), bypassPause: true));
        await mockTts.Started.WaitAsync(TimeSpan.FromSeconds(2));

        queue.EmergencyStop();
        await WaitUntilAsync(() => !queue.IsSpeaking);

        Assert.Equal(TtsPlaybackMode.Disarmed, queue.Mode);
        Assert.Equal(0, queue.Count);
        Assert.Equal(1, mockTts.SpeakCount);
    }

    [Fact]
    public async Task ManualMode_PlaysExactlyOneMessagePerAdvance()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.ArmAutomatic();
        queue.UseManualAdvance();
        Assert.True(queue.Enqueue(Approved("First")));
        Assert.True(queue.Enqueue(Approved("Second")));
        await Task.Delay(50);

        Assert.Equal(0, mockTts.SpeakCount);
        Assert.True(await queue.PlayNextManualAsync());
        Assert.Equal(1, mockTts.SpeakCount);
        Assert.Equal(1, queue.Count);
        Assert.True(await queue.PlayNextManualAsync());
        Assert.Equal(2, mockTts.SpeakCount);
        Assert.Equal(0, queue.Count);
        Assert.False(await queue.PlayNextManualAsync());
    }

    [Fact]
    public async Task ConcurrentManualAdvances_AreSerializedWithoutOverlap()
    {
        var mockTts = new BlockingTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.ArmAutomatic();
        queue.UseManualAdvance();
        Assert.True(queue.Enqueue(Approved("First")));
        Assert.True(queue.Enqueue(Approved("Second")));

        Task<bool> firstAdvance = queue.PlayNextManualAsync();
        await mockTts.Started.WaitAsync(TimeSpan.FromSeconds(2));
        Task<bool> secondAdvance = queue.PlayNextManualAsync();
        await Task.Delay(50);

        Assert.False(firstAdvance.IsCompleted);
        Assert.False(secondAdvance.IsCompleted);
        Assert.Equal(1, mockTts.SpeakCount);
        Assert.Equal(1, queue.Count);

        mockTts.Release();

        Assert.True(await firstAdvance.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(await secondAdvance.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(2, mockTts.SpeakCount);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task StopCurrentSpeech_CancelsOnlyCurrentItemAndKeepsArmedState()
    {
        var mockTts = new BlockingTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.ArmAutomatic();
        Assert.True(queue.Enqueue(Approved("Current")));
        await mockTts.Started.WaitAsync(TimeSpan.FromSeconds(2));

        queue.StopCurrentSpeech();
        await WaitUntilAsync(() => !queue.IsSpeaking);

        Assert.True(queue.IsArmed);
        Assert.True(mockTts.WasStopped);
        Assert.True(mockRouter.WasStopped);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task StopCurrentSpeech_PreservesPendingItemsAndManualMode()
    {
        var mockTts = new BlockingTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.ArmAutomatic();
        queue.UseManualAdvance();
        Assert.True(queue.Enqueue(Approved("Current")));
        Assert.True(queue.Enqueue(Approved("Pending")));
        Task<bool> currentAdvance = queue.PlayNextManualAsync();
        await mockTts.Started.WaitAsync(TimeSpan.FromSeconds(2));

        queue.StopCurrentSpeech();

        Assert.True(await currentAdvance.WaitAsync(TimeSpan.FromSeconds(2)));
        await WaitUntilAsync(() => !queue.IsSpeaking);
        Assert.Equal(TtsPlaybackMode.Manual, queue.Mode);
        Assert.True(queue.IsArmed);
        Assert.Equal(1, queue.Count);
        Assert.True(mockTts.WasStopped);
        Assert.True(mockRouter.WasStopped);
    }

    [Fact]
    public async Task EmergencyStop_DisarmsPurgesAndResetsPauseState()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.ArmAutomatic();
        queue.SetPaused(true);
        Assert.True(queue.Enqueue(Approved("Pending")));

        queue.EmergencyStop();

        Assert.Equal(TtsPlaybackMode.Disarmed, queue.Mode);
        Assert.False(queue.IsArmed);
        Assert.False(queue.IsAutoPlay);
        Assert.False(queue.IsPaused);
        Assert.Equal(0, queue.Count);
        Assert.True(mockTts.WasStopped);
        Assert.True(mockRouter.WasStopped);
        Assert.False(queue.Enqueue(Approved("After emergency")));
    }

    [Fact]
    public async Task EmergencyStop_DuringSpeechIsIdempotentAndRearmStartsCleanAutomaticMode()
    {
        var mockTts = new BlockingTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.ArmAutomatic();
        Assert.True(queue.Enqueue(Approved("Current")));
        Assert.True(queue.Enqueue(Approved("Must be cleared")));
        await mockTts.Started.WaitAsync(TimeSpan.FromSeconds(2));

        queue.EmergencyStop();
        queue.EmergencyStop();
        await WaitUntilAsync(() => !queue.IsSpeaking);

        Assert.Equal(TtsPlaybackMode.Disarmed, queue.Mode);
        Assert.False(queue.IsArmed);
        Assert.False(queue.IsAutoPlay);
        Assert.False(queue.IsPaused);
        Assert.Equal(0, queue.Count);
        Assert.Equal(1, mockTts.SpeakCount);
        Assert.True(mockTts.WasStopped);
        Assert.True(mockRouter.WasStopped);
        Assert.False(queue.Enqueue(Approved("Rejected while disarmed")));

        mockTts.Release();
        queue.ArmAutomatic();

        Assert.Equal(TtsPlaybackMode.Automatic, queue.Mode);
        Assert.True(queue.IsArmed);
        Assert.True(queue.IsAutoPlay);
        Assert.False(queue.IsPaused);
        Assert.True(queue.Enqueue(Approved("Fresh after re-arm")));
        await WaitUntilAsync(() => mockTts.SpeakCount == 2);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Enqueue_IgnoresRejectedDecisions()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);
        queue.ArmAutomatic();
        queue.UseManualAdvance();

        var rejectedDecision = new ModerationDecision
        {
            Message = new ChatMessage { RawText = "Toxic comment", Author = "bad_user" },
            Disposition = ModerationDisposition.Rejected,
            ReasonCode = ModerationReasonCode.BlockedTerm,
            SpokenText = ""
        };

        Assert.False(queue.Enqueue(rejectedDecision));
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Enqueue_RejectsItemsBeyondConfiguredCapacity()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter, capacity: 2);
        queue.ArmAutomatic();
        queue.UseManualAdvance();

        Assert.True(queue.Enqueue(Approved("First")));
        Assert.True(queue.Enqueue(Approved("Second")));
        Assert.False(queue.Enqueue(Approved("Third")));
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public async Task ManualPlaybackWhileDisarmed_DoesNotSpeak()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        Assert.False(await queue.PlayNextManualAsync());

        Assert.Equal(0, mockTts.SpeakCount);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task ClearQueue_RemovesPendingMessagesWithoutDisarming()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);
        queue.ArmAutomatic();
        queue.UseManualAdvance();
        Assert.True(queue.Enqueue(Approved("First")));
        Assert.True(queue.Enqueue(Approved("Second")));

        queue.ClearQueue();

        Assert.True(queue.IsArmed);
        Assert.Equal(TtsPlaybackMode.Manual, queue.Mode);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task ClearQueue_DoesNotInterruptCurrentSpeech()
    {
        var mockTts = new BlockingTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.ArmAutomatic();
        Assert.True(queue.Enqueue(Approved("Current")));
        Assert.True(queue.Enqueue(Approved("Pending")));
        await mockTts.Started.WaitAsync(TimeSpan.FromSeconds(2));

        queue.ClearQueue();

        Assert.True(queue.IsSpeaking);
        Assert.Equal(TtsPlaybackMode.Automatic, queue.Mode);
        Assert.Equal(0, queue.Count);
        Assert.False(mockTts.WasStopped);
        Assert.False(mockRouter.WasStopped);

        mockTts.Release();
        await WaitUntilAsync(() => !queue.IsSpeaking);
        Assert.Equal(1, mockTts.SpeakCount);
        Assert.Equal(1, mockRouter.PlaybackCount);
        Assert.Equal(0, queue.Count);
    }

    private static ModerationDecision Approved(string text) => new()
    {
        Message = new ChatMessage { RawText = text, Author = "viewer" },
        Disposition = ModerationDisposition.Approved,
        SpokenText = text
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
