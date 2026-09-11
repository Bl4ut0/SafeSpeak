using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public sealed class ModerationPipelineLifetimeTests
{
    [Fact]
    public async Task Dispose_ReleasesClassifierOnce_AndRejectsFurtherProcessing()
    {
        var classifier = new TrackingClassifier();
        var pipeline = new ModerationPipeline(
            new ModerationConfig { UserCooldownSeconds = 0 },
            intentClassifier: classifier);

        pipeline.Dispose();
        pipeline.Dispose();

        Assert.Equal(1, classifier.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            pipeline.ProcessMessageAsync(new ChatMessage
            {
                Author = "viewer",
                RawText = "hello"
            }));
    }

    [Fact]
    public async Task ProcessMessageAsync_FastPathsSystemEventsWithoutClassifierInference()
    {
        var classifier = new TrackingClassifier();
        using var pipeline = new ModerationPipeline(
            new ModerationConfig { UserCooldownSeconds = 0 },
            intentClassifier: classifier);

        var systemMessage = new ChatMessage
        {
            Author = "tiktok_system",
            RawText = "User followed the host"
        };

        var decision = await pipeline.ProcessMessageAsync(systemMessage, isSystemEvent: true);

        Assert.True(decision.Passed);
        Assert.Equal(0, classifier.ClassifyCount);

        var normalMessage = new ChatMessage
        {
            Author = "viewer",
            RawText = "Hello world"
        };

        var normalDecision = await pipeline.ProcessMessageAsync(normalMessage, isSystemEvent: false);

        Assert.True(normalDecision.Passed);
        Assert.Equal(1, classifier.ClassifyCount);
    }

    private sealed class TrackingClassifier : IIntentClassifier
    {
        private int _disposeCount;
        private int _classifyCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public int ClassifyCount => Volatile.Read(ref _classifyCount);
        public string ModelName => "Tracking classifier";
        public bool IsModelLoaded => true;

        public Task<IntentClassificationResult> ClassifyAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _classifyCount);
            return Task.FromResult(new IntentClassificationResult
            {
                IsToxic = false,
                ToxicityScore = 0.0,
                ModelUsed = "Tracking classifier"
            });
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
