using SafeSpeak.Core.Moderation;
using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class RuleEngineRateLimitTests
{
    [Fact]
    public void PerUserSlidingWindow_AllowsConfiguredCountThenRejectsUntilWindowExpires()
    {
        var rules = new RuleEngine();
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-06T12:00:00Z");

        Assert.Equal(RuleEngine.MessageRateResult.Allowed,
            rules.TryAcceptMessageRate("viewer", 10, 2, 100, start));
        Assert.Equal(RuleEngine.MessageRateResult.Allowed,
            rules.TryAcceptMessageRate("viewer", 10, 2, 100, start.AddSeconds(1)));
        Assert.Equal(RuleEngine.MessageRateResult.UserLimitExceeded,
            rules.TryAcceptMessageRate("viewer", 10, 2, 100, start.AddSeconds(2)));
        Assert.Equal(RuleEngine.MessageRateResult.Allowed,
            rules.TryAcceptMessageRate("viewer", 10, 2, 100, start.AddSeconds(10)));
    }

    [Fact]
    public void StreamSlidingWindow_AggregatesDifferentViewers()
    {
        var rules = new RuleEngine();
        DateTimeOffset now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");

        Assert.Equal(RuleEngine.MessageRateResult.Allowed,
            rules.TryAcceptMessageRate("one", 1, 5, 2, now));
        Assert.Equal(RuleEngine.MessageRateResult.Allowed,
            rules.TryAcceptMessageRate("two", 1, 5, 2, now));
        Assert.Equal(RuleEngine.MessageRateResult.StreamLimitExceeded,
            rules.TryAcceptMessageRate("three", 1, 5, 2, now));
    }

    [Fact]
    public void EmptyLocalTestIdentity_DoesNotConsumeLiveRateLimit()
    {
        var rules = new RuleEngine();
        DateTimeOffset now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");

        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(RuleEngine.MessageRateResult.Allowed,
                rules.TryAcceptMessageRate(string.Empty, 10, 1, 1, now));
        }

        Assert.Equal(RuleEngine.MessageRateResult.Allowed,
            rules.TryAcceptMessageRate("viewer", 10, 1, 1, now));
    }

    [Fact]
    public async Task ModerationPipeline_RejectsSecondViewerMessageBeforeClassification()
    {
        using var classifier = new CountingCleanClassifier();
        using var pipeline = new ModerationPipeline(
            new ModerationConfig
            {
                UserCooldownSeconds = 0,
                MessageRateLimitEnabled = true,
                MessageRateWindow = MessageRateWindow.TenSeconds,
                PerUserMessageLimit = 1,
                StreamMessageLimit = 500
            },
            intentClassifier: classifier);
        var message = new ChatMessage
        {
            Author = "viewer",
            AuthorDisplayName = "Viewer",
            RawText = "hello stream"
        };

        ModerationDecision first = await pipeline.ProcessMessageAsync(message);
        ModerationDecision second = await pipeline.ProcessMessageAsync(
            message with { RawText = "another message" });

        Assert.True(first.Passed);
        Assert.False(second.Passed);
        Assert.Equal(ModerationReasonCode.UserCooldown, second.ReasonCode);
        Assert.Equal(2, classifier.Count); // Message plus safe display name for the first item only.
    }

    private sealed class CountingCleanClassifier : IIntentClassifier
    {
        private int _count;
        public string ModelName => "Clean test classifier";
        public bool IsModelLoaded => true;
        public int Count => Volatile.Read(ref _count);

        public Task<IntentClassificationResult> ClassifyAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _count);
            return Task.FromResult(new IntentClassificationResult
            {
                FlaggedCategory = "None",
                ModelUsed = ModelName
            });
        }

        public void Dispose()
        {
        }
    }
}
