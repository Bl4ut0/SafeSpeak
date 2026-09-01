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

    private sealed class TrackingClassifier : IIntentClassifier
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public string ModelName => "Tracking classifier";
        public bool IsModelLoaded => true;

        public Task<IntentClassificationResult> ClassifyAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IntentClassificationResult());

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
