using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

[CollectionDefinition("Local moderation runtime", DisableParallelization = true)]
public sealed class LocalModerationRuntimeCollection;

[Collection("Local moderation runtime")]
public sealed class LocalOnnxIntentClassifierTests
{
    [Fact]
    public async Task BundledModelLoadsAndSeparatesCleanFromHostileText()
    {
        using var classifier = new LocalOnnxIntentClassifier();

        IntentClassificationResult clean = await classifier.ClassifyAsync("great stream today");
        IntentClassificationResult hostile = await classifier.ClassifyAsync("fuck off");

        Assert.True(classifier.IsModelLoaded, classifier.AvailabilityMessage);
        Assert.Contains("MiniLM Local ONNX", hostile.ModelUsed, StringComparison.Ordinal);
        Assert.True(clean.ToxicityScore < 0.20, $"Clean score was {clean.ToxicityScore:F4}");
        Assert.True(hostile.ToxicityScore > 0.90, $"Hostile score was {hostile.ToxicityScore:F4}");
        Assert.All(
            new[]
            {
                clean.ToxicityScore,
                clean.SevereToxicityScore,
                clean.ObsceneScore,
                clean.ThreatScore,
                clean.HarassmentScore,
                clean.InsultScore,
                clean.IdentityHateScore,
                hostile.ToxicityScore,
                hostile.SevereToxicityScore,
                hostile.ObsceneScore,
                hostile.ThreatScore,
                hostile.HarassmentScore,
                hostile.InsultScore,
                hostile.IdentityHateScore
            },
            score => Assert.InRange(score, 0, 1));
    }

    [Fact]
    public async Task PositiveProfanityIsNotTreatedAsHostileIntent()
    {
        using var classifier = new LocalOnnxIntentClassifier();

        IntentClassificationResult result = await classifier.ClassifyAsync("this is fucking amazing");

        Assert.InRange(
            result.ToxicityScore,
            ModerationConfig.GetIntentToxicityThreshold(4),
            ModerationConfig.GetIntentToxicityThreshold(3) - 0.001);
        Assert.False(result.IsToxic);
    }

    [Fact]
    public async Task StrongAllowsExactPositiveEmphasisButMaximumStillBlocksIt()
    {
        using var strong = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = 3,
            UserCooldownSeconds = 0
        });
        using var maximum = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = 4,
            UserCooldownSeconds = 0
        });

        ModerationDecision strongDecision = await strong.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer-id",
            AuthorDisplayName = "Friendly Viewer",
            RawText = "this is fucking amazing"
        });
        ModerationDecision maximumDecision = await maximum.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer-id-2",
            AuthorDisplayName = "Friendly Viewer",
            RawText = "this is fucking amazing"
        });

        Assert.True(strongDecision.Passed);
        Assert.False(maximumDecision.Passed);
    }

    [Theory]
    [InlineData("fuck off")]
    [InlineData("you are a fucking idiot")]
    [InlineData("this is fucking amazing, fuck off")]
    [InlineData("this is fucking amazing you idiot")]
    [InlineData("this is fucking amazing, I will kill you")]
    public async Task PositiveContextPolicyCannotHideHostility(string text)
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = 3,
            UserCooldownSeconds = 0
        });

        ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer-id",
            AuthorDisplayName = "Friendly Viewer",
            RawText = text
        });

        Assert.False(decision.Passed);
        Assert.Empty(decision.SpokenText);
    }

    [Fact]
    public async Task CustomBlockedTermOverridesPositiveContextPolicy()
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = 3,
            UserCooldownSeconds = 0,
            CustomBlockedTerms = ["fucking"]
        });

        ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer-id",
            AuthorDisplayName = "Friendly Viewer",
            RawText = "this is fucking amazing"
        });

        Assert.False(decision.Passed);
        Assert.Equal(ModerationReasonCode.BlockedTerm, decision.ReasonCode);
    }

    [Fact]
    public async Task CommonCjkTextProducesFiniteScoresWhenLanguageRestrictionIsDisabled()
    {
        using var classifier = new LocalOnnxIntentClassifier();

        IntentClassificationResult result = await classifier.ClassifyAsync("这场直播很好");

        Assert.True(double.IsFinite(result.ToxicityScore));
        Assert.InRange(result.ToxicityScore, 0, 1);
    }

    [Fact]
    public async Task CleanDisplayNameIsSpokenWithChat()
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = 3,
            UserCooldownSeconds = 0
        });

        ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "stream_fan_42",
            AuthorDisplayName = "Stream Fan",
            RawText = "Hello everyone"
        });

        Assert.True(decision.Passed);
        Assert.Equal("Stream Fan", decision.SafeAuthorDisplayName);
        Assert.Equal("Stream Fan says: Hello everyone", decision.SpokenText);
    }

    [Theory]
    [InlineData("fuuuuuck off")]
    [InlineData("f u c k off")]
    [InlineData("f\u200Buck off")]
    [InlineData("k y s")]
    public async Task HostileOrEvasiveDisplayNameIsReplacedBeforeSpeech(string displayName)
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = 3,
            UserCooldownSeconds = 0
        });

        ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer-id",
            AuthorDisplayName = displayName,
            RawText = "Hello streamer"
        });

        Assert.True(decision.Passed);
        Assert.Equal("A viewer", decision.SafeAuthorDisplayName);
        Assert.Equal("A viewer says: Hello streamer", decision.SpokenText);
        Assert.DoesNotContain(displayName, decision.AccessibleSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonChatAttributionUsesSanitizedLeadingName()
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = 3,
            UserCooldownSeconds = 0
        });

        ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer-id",
            AuthorDisplayName = "fuuuuuck off",
            RawText = "followed the stream",
            AttributionStyle = SpokenAttributionStyle.LeadingName
        });

        Assert.True(decision.Passed);
        Assert.Equal("A viewer followed the stream", decision.SpokenText);
    }

    [Fact]
    public async Task ConcurrentClassifiersShareRuntimeUntilLastOwnerDisposesIt()
    {
        Assert.Equal(0, LocalOnnxIntentClassifier.ActiveRuntimeLeaseCount);
        Assert.False(LocalOnnxIntentClassifier.HasActiveRuntime);
        long createsBefore = LocalOnnxIntentClassifier.RuntimeCreateCount;
        long disposalsBefore = LocalOnnxIntentClassifier.RuntimeDisposeCount;

        var first = new LocalOnnxIntentClassifier();
        var second = new LocalOnnxIntentClassifier();
        try
        {
            Assert.True(first.IsModelLoaded, first.AvailabilityMessage);
            Assert.True(second.IsModelLoaded, second.AvailabilityMessage);
            Assert.Equal(2, LocalOnnxIntentClassifier.ActiveRuntimeLeaseCount);

            IntentClassificationResult[] results = await Task.WhenAll(
                first.ClassifyAsync("great stream today"),
                first.ClassifyAsync("fuck off"),
                second.ClassifyAsync("hello everyone"),
                second.ClassifyAsync("you are an idiot"));

            Assert.Equal(2, LocalOnnxIntentClassifier.ActiveRuntimeLeaseCount);
            Assert.Contains(results, result => result.ToxicityScore > 0.90);

            first.Dispose();
            first.Dispose();
            Assert.Equal(1, LocalOnnxIntentClassifier.ActiveRuntimeLeaseCount);
            Assert.True(LocalOnnxIntentClassifier.HasActiveRuntime);

            IntentClassificationResult stillUsable = await second.ClassifyAsync("great stream today");
            Assert.True(stillUsable.ToxicityScore < 0.20);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }

        Assert.Equal(0, LocalOnnxIntentClassifier.ActiveRuntimeLeaseCount);
        Assert.False(LocalOnnxIntentClassifier.HasActiveRuntime);
        Assert.Equal(createsBefore + 1, LocalOnnxIntentClassifier.RuntimeCreateCount);
        Assert.Equal(disposalsBefore + 1, LocalOnnxIntentClassifier.RuntimeDisposeCount);
    }

    [Fact]
    public async Task DisposedClassifierRejectsFurtherInferenceWithoutReloadingRuntime()
    {
        var classifier = new LocalOnnxIntentClassifier();
        Assert.True(classifier.IsModelLoaded, classifier.AvailabilityMessage);
        classifier.Dispose();
        classifier.Dispose();

        Assert.False(LocalOnnxIntentClassifier.HasActiveRuntime);
        Assert.Throws<ObjectDisposedException>(() => _ = classifier.IsModelLoaded);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => classifier.ClassifyAsync("hello after shutdown"));
    }
}
