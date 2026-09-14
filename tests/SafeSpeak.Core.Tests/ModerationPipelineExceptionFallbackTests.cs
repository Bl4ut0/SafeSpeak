using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public sealed class ModerationPipelineExceptionFallbackTests
{
    [Fact]
    public async Task ProcessMessageAsync_ReturnsRejectedDecision_WhenClassifierThrowsGenericException()
    {
        var config = new ModerationConfig
        {
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(
            config,
            intentClassifier: new ThrowingClassifier(new InvalidOperationException("Classifier failed internally.")));

        var chatMessage = new ChatMessage
        {
            Author = "viewer",
            AuthorDisplayName = "Viewer",
            RawText = "This message is fine, but the classifier fails."
        };

        var decision = await pipeline.ProcessMessageAsync(chatMessage);

        Assert.Equal(ModerationDisposition.Rejected, decision.Disposition);
        Assert.Equal(ModerationReasonCode.SevereToxicity, decision.ReasonCode);
        Assert.Equal("The contextual safety layer was unavailable", decision.ReasonDescription);
        Assert.Empty(decision.SpokenText);
    }

    [Fact]
    public async Task ProcessMessageAsync_RethrowsOperationCanceledException_WhenClassifierThrowsIt()
    {
        var config = new ModerationConfig
        {
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(
            config,
            intentClassifier: new ThrowingClassifier(new OperationCanceledException()));

        var chatMessage = new ChatMessage
        {
            Author = "viewer",
            AuthorDisplayName = "Viewer",
            RawText = "This message is fine, but the classification is cancelled."
        };

        var tokenSource = new CancellationTokenSource();
        tokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.ProcessMessageAsync(chatMessage, tokenSource.Token));
    }

    private sealed class ThrowingClassifier : IIntentClassifier
    {
        private readonly Exception _exceptionToThrow;

        public ThrowingClassifier(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public string ModelName => "Throwing";
        public bool IsModelLoaded => true;

        public Task<IntentClassificationResult> ClassifyAsync(string text, CancellationToken cancellationToken = default)
        {
            // Throw instead of returning a faulted task to simulate the catch block logic precisely,
            // though async machinery usually wraps it.
            throw _exceptionToThrow;
        }

        public void Dispose() { }
    }
}
