using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public class MockTtsEngine : ITtsEngine
{
    public int SpeakCount { get; private set; }
    public bool WasStopped { get; private set; }

    public IReadOnlyList<VoiceInfo> GetAvailableVoices() => new List<VoiceInfo>
    {
        new("mock_voice", "Mock Voice", "Mock Provider", "en-US", "Neutral", "Mock test voice", false)
    };

    public Task SynthesizeToWaveStreamAsync(string text, Stream outputStream, string? voiceName = null, int rate = 0, int volume = 100, CancellationToken cancellationToken = default)
    {
        SpeakCount++;
        // Write simple mock WAV header
        byte[] dummyWav = new byte[44];
        outputStream.Write(dummyWav, 0, dummyWav.Length);
        return Task.CompletedTask;
    }

    public Task SpeakDirectAsync(string text, string? voiceName = null, int rate = 0, int volume = 100, CancellationToken cancellationToken = default)
    {
        SpeakCount++;
        return Task.CompletedTask;
    }

    public void Stop()
    {
        WasStopped = true;
    }

    public void Dispose() { }
}

public class MockAudioRouter : IAudioRouter
{
    public string? SelectedEndpointId { get; private set; }
    public bool WasStopped { get; private set; }
    public int PlaybackCount { get; private set; }

    public IReadOnlyList<AudioEndpointInfo> GetOutputEndpoints() => new List<AudioEndpointInfo>
    {
        new("mock_device_1", "Mock Virtual Cable", true, true)
    };

    public void SelectEndpoint(string? endpointId)
    {
        SelectedEndpointId = endpointId;
    }

    public Task PlayWaveStreamAsync(Stream waveStream, float volume = 1, CancellationToken cancellationToken = default)
    {
        PlaybackCount++;
        return Task.CompletedTask;
    }

    public void Stop()
    {
        WasStopped = true;
    }

    public void Dispose() { }
}

public class TtsQueueTests
{
    [Fact]
    public async Task TtsQueue_StartsDisarmedByDefault()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        Assert.False(queue.IsArmed);
        Assert.False(queue.IsAutoPlay);
    }

    [Fact]
    public async Task EmergencyPanicFlush_DisarmsAndPurgesQueueImmediately()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.SetArmed(true);
        Assert.True(queue.IsArmed);

        var decision = new ModerationDecision
        {
            Message = new ChatMessage { RawText = "Hello", Author = "user1" },
            Disposition = ModerationDisposition.Approved,
            SpokenText = "Hello"
        };

        queue.Enqueue(decision);
        queue.EmergencyPanicFlush();

        Assert.False(queue.IsArmed);
        Assert.False(queue.IsAutoPlay);
        Assert.Equal(0, queue.Count);
        Assert.True(mockTts.WasStopped);
        Assert.True(mockRouter.WasStopped);
    }

    [Fact]
    public async Task Enqueue_IgnoresRejectedDecisions()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        var rejectedDecision = new ModerationDecision
        {
            Message = new ChatMessage { RawText = "Toxic comment", Author = "bad_user" },
            Disposition = ModerationDisposition.Rejected,
            ReasonCode = ModerationReasonCode.BlockedTerm,
            SpokenText = ""
        };

        queue.Enqueue(rejectedDecision);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Enqueue_RejectsItemsBeyondConfiguredCapacity()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter, capacity: 2);

        Assert.True(queue.Enqueue(Approved("First")));
        Assert.True(queue.Enqueue(Approved("Second")));
        Assert.False(queue.Enqueue(Approved("Third")));
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public async Task EnqueuedWhileDisarmed_PlaysAfterArmingAndEnablingAutoPlay()
    {
        var mockTts = new MockTtsEngine();
        var mockRouter = new MockAudioRouter();
        await using var queue = new TtsQueue(mockTts, mockRouter);

        queue.Enqueue(Approved("First"));
        queue.Enqueue(Approved("Second"));
        await Task.Delay(50);
        Assert.Equal(0, mockTts.SpeakCount);

        queue.SetArmed(true);
        queue.SetAutoPlay(true);

        await WaitUntilAsync(() => mockTts.SpeakCount == 2);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task PrivateMonitor_MirrorsApprovedSpeechToSecondEndpoint()
    {
        var mockTts = new MockTtsEngine();
        var broadcast = new MockAudioRouter();
        var privateMonitor = new MockAudioRouter();
        broadcast.SelectEndpoint("broadcast");
        privateMonitor.SelectEndpoint("headphones");
        await using var queue = new TtsQueue(mockTts, broadcast, privateAudioRouter: privateMonitor)
        {
            BroadcastOutputEnabled = true,
            PrivateMonitorEnabled = true,
            MirrorToPrivateMonitor = true
        };

        queue.Enqueue(Approved("Hello"));
        await queue.PlayNextManualAsync();

        Assert.Equal(1, broadcast.PlaybackCount);
        Assert.Equal(1, privateMonitor.PlaybackCount);
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
