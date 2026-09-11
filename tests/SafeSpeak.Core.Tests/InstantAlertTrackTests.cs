using System.Collections.Concurrent;
using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public sealed class InstantAlertTrackTests
{
    [Fact]
    public async Task PositiveCommunityEvents_BypassMessageRateLimit_WhileStandardChatIsThrottled()
    {
        var config = new ModerationConfig
        {
            MessageRateLimitEnabled = true,
            PerUserMessageLimit = 1,
            StreamMessageLimit = 100,
            MessageRateWindow = MessageRateWindow.TenSeconds,
            UserCooldownSeconds = 0,
            AudienceMode = AudienceMode.All
        };

        using var pipeline = new ModerationPipeline(config);

        // 1. First chat message from alice passes.
        var chat1 = new ChatMessage
        {
            Author = "alice",
            RawText = "Hello stream",
            EventType = LivestreamEventType.Chat
        };
        var decision1 = await pipeline.ProcessMessageAsync(chat1);
        Assert.True(decision1.Passed);

        // 2. Second chat message from alice within rate window is rate-limited.
        var chat2 = new ChatMessage
        {
            Author = "alice",
            RawText = "Second message so fast",
            EventType = LivestreamEventType.Chat
        };
        var decision2 = await pipeline.ProcessMessageAsync(chat2);
        Assert.False(decision2.Passed);
        Assert.Equal(ModerationReasonCode.UserCooldown, decision2.ReasonCode);

        // 3. A gift from alice bypasses rate limiting and passes.
        var gift = new ChatMessage
        {
            Author = "alice",
            RawText = "alice sent 5 roses",
            EventType = LivestreamEventType.Gift,
            IsDonor = true
        };
        var giftDecision = await pipeline.ProcessMessageAsync(gift);
        Assert.True(giftDecision.Passed);

        // 4. A follow from alice bypasses rate limiting and passes.
        var follow = new ChatMessage
        {
            Author = "alice",
            RawText = "alice followed the stream",
            EventType = LivestreamEventType.Follow
        };
        var followDecision = await pipeline.ProcessMessageAsync(follow);
        Assert.True(followDecision.Passed);
    }

    [Fact]
    public async Task PositiveCommunityEvents_BypassUserCooldown_WhileStandardChatIsBlocked()
    {
        var config = new ModerationConfig
        {
            MessageRateLimitEnabled = false,
            UserCooldownSeconds = 60,
            AudienceMode = AudienceMode.All
        };

        using var pipeline = new ModerationPipeline(config);

        // First message activates cooldown.
        var chat1 = new ChatMessage
        {
            Author = "bob",
            RawText = "First message",
            EventType = LivestreamEventType.Chat
        };
        var decision1 = await pipeline.ProcessMessageAsync(chat1);
        Assert.True(decision1.Passed);

        // Second chat message hit by cooldown.
        var chat2 = new ChatMessage
        {
            Author = "bob",
            RawText = "Cooldown chat",
            EventType = LivestreamEventType.Chat
        };
        var decision2 = await pipeline.ProcessMessageAsync(chat2);
        Assert.False(decision2.Passed);
        Assert.Equal(ModerationReasonCode.UserCooldown, decision2.ReasonCode);

        // Follow from bob during cooldown passes.
        var follow = new ChatMessage
        {
            Author = "bob",
            RawText = "bob followed the stream",
            EventType = LivestreamEventType.Follow
        };
        var followDecision = await pipeline.ProcessMessageAsync(follow);
        Assert.True(followDecision.Passed);

        // Gift from bob during cooldown passes.
        var gift = new ChatMessage
        {
            Author = "bob",
            RawText = "bob sent 1 galaxy",
            EventType = LivestreamEventType.Gift,
            IsDonor = true
        };
        var giftDecision = await pipeline.ProcessMessageAsync(gift);
        Assert.True(giftDecision.Passed);
    }

    [Fact]
    public async Task DualTrackQueues_CanPlayConcurrently_AndRespectFifoOrder()
    {
        var track1Engine = new SequenceTrackingTtsEngine();
        var track1Router = new MockAudioRouter();
        await using var mainTrack = new TtsQueue(track1Engine, track1Router);

        var track2Engine = new SequenceTrackingTtsEngine();
        var track2Router = new MockAudioRouter();
        await using var alertTrack = new TtsQueue(track2Engine, track2Router);

        mainTrack.ArmAutomatic();
        alertTrack.ArmAutomatic();

        // Enqueue items on Track 2 in order: Alert 1, Alert 2, Alert 3
        var alert1 = CreateDecision("Gift 1");
        var alert2 = CreateDecision("Follow 2");
        var alert3 = CreateDecision("Gift 3");

        alertTrack.Enqueue(alert1);
        alertTrack.Enqueue(alert2);
        alertTrack.Enqueue(alert3);

        // Also enqueue on Track 1 to verify concurrent existence
        var chat1 = CreateDecision("Chat message");
        mainTrack.Enqueue(chat1);

        // Wait for all items to complete playback
        await WaitForConditionAsync(() =>
            alertTrack.Count == 0 &&
            !alertTrack.IsSpeaking &&
            track2Engine.SpokenItems.Count == 3,
            TimeSpan.FromSeconds(5));

        // Verify Track 2 strictly processed in FIFO order
        var spokenList = track2Engine.SpokenItems.ToArray();
        Assert.Equal(3, spokenList.Length);
        Assert.Equal("Gift 1", spokenList[0]);
        Assert.Equal("Follow 2", spokenList[1]);
        Assert.Equal("Gift 3", spokenList[2]);
    }

    [Fact]
    public async Task EmergencyStop_ClearsAndStopsBothTracks()
    {
        var track1Engine = new MockTtsEngine();
        var track1Router = new MockAudioRouter();
        await using var mainTrack = new TtsQueue(track1Engine, track1Router);

        var track2Engine = new MockTtsEngine();
        var track2Router = new MockAudioRouter();
        await using var alertTrack = new TtsQueue(track2Engine, track2Router);

        mainTrack.ArmAutomatic();
        alertTrack.ArmAutomatic();

        mainTrack.Enqueue(CreateDecision("Chat 1"));
        mainTrack.Enqueue(CreateDecision("Chat 2"));
        alertTrack.Enqueue(CreateDecision("Alert 1"));
        alertTrack.Enqueue(CreateDecision("Alert 2"));

        mainTrack.EmergencyStop();
        alertTrack.EmergencyStop();

        Assert.False(mainTrack.IsArmed);
        Assert.False(alertTrack.IsArmed);
        Assert.Equal(0, mainTrack.Count);
        Assert.Equal(0, alertTrack.Count);
    }

    private static ModerationDecision CreateDecision(string text) => new()
    {
        Message = new ChatMessage { Author = "user", RawText = text },
        Disposition = ModerationDisposition.Approved,
        SpokenText = text
    };

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not met within timeout.");
            }
            await Task.Delay(20);
        }
    }

    private sealed class SequenceTrackingTtsEngine : ITtsEngine
    {
        public ConcurrentQueue<string> SpokenItems { get; } = new();

        public IReadOnlyList<VoiceInfo> GetAvailableVoices() => [];

        public async Task SynthesizeToWaveStreamAsync(
            string text,
            Stream outputStream,
            string? voiceName = null,
            int rate = 0,
            int volume = 100,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpokenItems.Enqueue(text);
            await Task.Delay(10, cancellationToken);
            await outputStream.WriteAsync(new byte[44], cancellationToken);
        }

        public Task SpeakDirectAsync(
            string text,
            string? voiceName = null,
            int rate = 0,
            int volume = 100,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpokenItems.Enqueue(text);
            return Task.CompletedTask;
        }

        public void Stop() { }
        public void Dispose() { }
    }
}
