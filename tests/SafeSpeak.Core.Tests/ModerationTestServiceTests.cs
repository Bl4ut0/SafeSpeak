using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public sealed class ModerationTestServiceTests
{
    [Fact]
    public async Task EvaluateAsync_AllowsCleanSampleThroughProductionPipeline()
    {
        using var classifier = new FixedClassifier(CleanResult());
        using var pipeline = CreatePipeline(classifier);
        var service = new ModerationTestService(pipeline);

        ModerationTestResult result =
            await service.EvaluateAsync("Thanks for the helpful stream");

        Assert.Equal(ModerationTestOutcome.Allowed, result.Outcome);
        Assert.True(result.IsAllowed);
        Assert.Equal("Allowed", result.Category);
        Assert.Equal("Approved", result.Reason);
        Assert.Equal(
            "Allowed — this message passes the current safety settings.",
            result.AccessibleSummary);
    }

    [Theory]
    [InlineData("kill yourself")]
    [InlineData("k y s right now")]
    public async Task EvaluateAsync_BlocksBuiltInAndEvasiveRules(string sample)
    {
        using var classifier = new FixedClassifier(CleanResult());
        using var pipeline = CreatePipeline(classifier);
        var service = new ModerationTestService(pipeline);

        ModerationTestResult result = await service.EvaluateAsync(sample);

        Assert.Equal(ModerationTestOutcome.Blocked, result.Outcome);
        Assert.False(result.IsAllowed);
        Assert.Equal("Blocked safety rule", result.Category);
        Assert.Equal(
            "Message matched a blocked safety rule.",
            result.Reason);
        Assert.Equal(
            "Blocked — Blocked safety rule. Message matched a blocked safety rule.",
            result.AccessibleSummary);
        Assert.Equal(0, classifier.ClassificationCount);
    }

    [Fact]
    public async Task EvaluateAsync_BlocksConfiguredCustomRule()
    {
        using var classifier = new FixedClassifier(CleanResult());
        using var pipeline = CreatePipeline(
            classifier,
            customBlockedTerms: ["spoiler phrase"]);
        var service = new ModerationTestService(pipeline);

        ModerationTestResult result =
            await service.EvaluateAsync("This contains a spoiler phrase today");

        Assert.Equal(ModerationTestOutcome.Blocked, result.Outcome);
        Assert.Equal("Blocked safety rule", result.Category);
        Assert.DoesNotContain(
            "spoiler phrase",
            result.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateAsync_BlocksModelResultWithFixedPrivacySafeCategory()
    {
        const string hostileSample = "raw hostile sample must stay private";
        using var classifier = new FixedClassifier(new IntentClassificationResult
        {
            IsToxic = true,
            ToxicityScore = 0.95,
            HarassmentScore = 0.91,
            FlaggedCategory = hostileSample,
            ModelUsed = "Deterministic test"
        });
        using var pipeline = CreatePipeline(classifier);
        var service = new ModerationTestService(pipeline);

        ModerationTestResult result =
            await service.EvaluateAsync(hostileSample);

        Assert.Equal(ModerationTestOutcome.Blocked, result.Outcome);
        Assert.Equal("Threat or harassment", result.Category);
        Assert.Equal(
            "Message contains suspected threat or harassment content.",
            result.Reason);
        Assert.DoesNotContain(hostileSample, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            hostileSample,
            result.AccessibleSummary,
            StringComparison.Ordinal);
        Assert.DoesNotContain("0.95", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_RepeatedSamplesDoNotContaminateCooldownState()
    {
        using var classifier = new FixedClassifier(CleanResult());
        using var pipeline = CreatePipeline(
            classifier,
            audienceMode: AudienceMode.ModeratorsOnly,
            cooldownSeconds: 600);
        var service = new ModerationTestService(pipeline);

        ModerationTestResult first =
            await service.EvaluateAsync("A clean repeated sample");
        ModerationTestResult second =
            await service.EvaluateAsync("A clean repeated sample");

        Assert.Equal(ModerationTestOutcome.Allowed, first.Outcome);
        Assert.Equal(ModerationTestOutcome.Allowed, second.Outcome);
        Assert.Equal(2, classifier.ClassificationCount);
    }

    [Fact]
    public async Task EvaluateAsync_PropagatesCancellation()
    {
        using var classifier = new FixedClassifier(CleanResult());
        using var pipeline = CreatePipeline(classifier);
        var service = new ModerationTestService(pipeline);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.EvaluateAsync("A sample", cancellation.Token));

        Assert.Equal(0, classifier.ClassificationCount);
    }

    [Fact]
    public async Task Service_DoesNotOwnOrDisposeInjectedPipeline()
    {
        var classifier = new FixedClassifier(CleanResult());
        var pipeline = CreatePipeline(classifier);
        var service = new ModerationTestService(pipeline);

        Assert.DoesNotContain(
            typeof(IDisposable),
            typeof(ModerationTestService).GetInterfaces());
        _ = await service.EvaluateAsync("First clean sample");
        Assert.False(classifier.IsDisposed);

        ModerationDecision directDecision = await pipeline.ProcessMessageAsync(
            new ChatMessage
            {
                Author = string.Empty,
                AuthorTier = AuthorTier.Host,
                RawText = "Second clean sample"
            });

        Assert.True(directDecision.Passed);
        Assert.False(classifier.IsDisposed);

        pipeline.Dispose();
        Assert.True(classifier.IsDisposed);
    }

    private static ModerationPipeline CreatePipeline(
        IIntentClassifier classifier,
        IReadOnlyList<string>? customBlockedTerms = null,
        AudienceMode audienceMode = AudienceMode.All,
        int cooldownSeconds = 5)
    {
        var config = new ModerationConfig
        {
            AudienceMode = audienceMode,
            UserCooldownSeconds = cooldownSeconds,
            IntentModerationLevel = 3
        };
        if (customBlockedTerms is not null)
            config.CustomBlockedTerms.AddRange(customBlockedTerms);

        return new ModerationPipeline(config, intentClassifier: classifier);
    }

    private static IntentClassificationResult CleanResult() => new()
    {
        FlaggedCategory = "None",
        ModelUsed = "Deterministic test"
    };

    private sealed class FixedClassifier(IntentClassificationResult result)
        : IIntentClassifier
    {
        private int _classificationCount;

        public string ModelName => "Fixed moderation test classifier";
        public bool IsModelLoaded => true;
        public int ClassificationCount => Volatile.Read(ref _classificationCount);
        public bool IsDisposed { get; private set; }

        public Task<IntentClassificationResult> ClassifyAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _classificationCount);
            return Task.FromResult(result);
        }

        public void Dispose() => IsDisposed = true;
    }
}
