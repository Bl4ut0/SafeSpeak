using SafeSpeak.Core.Chat;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests.Moderation;

public sealed class ModerationPipelineTests
{
    [Fact]
    public async Task ApprovesOrdinaryEnglishMessage()
    {
        ModerationDecision result = await CreatePipeline().EvaluateAsync(Message("Hello everyone"));

        Assert.Equal(ModerationDisposition.Approved, result.Disposition);
        Assert.Equal("Hello everyone", result.SpeakableText);
    }

    [Theory]
    [InlineData("badword")]
    [InlineData("b a d w o r d")]
    [InlineData("b4dw0rd")]
    [InlineData("bad\u200Bword")]
    public async Task RejectsNormalizedBlocklistMatches(string text)
    {
        ModerationDecision result = await CreatePipeline().EvaluateAsync(Message(text));

        Assert.Equal(ModerationDisposition.Rejected, result.Disposition);
        Assert.Equal(ModerationReason.BlockedTerm, result.Reason);
    }

    [Fact]
    public async Task RejectsNonLatinScriptInEnglishOnlyMode()
    {
        ModerationDecision result = await CreatePipeline().EvaluateAsync(Message("\u0e17\u0e14\u0e2a\u0e2d\u0e1a"));

        Assert.Equal(ModerationReason.DisallowedScript, result.Reason);
    }

    [Fact]
    public async Task RejectsMixedScriptsWhenEnglishOnlyIsDisabled()
    {
        ModerationPipeline pipeline = CreatePipeline(new ModerationOptions
        {
            EnglishOnly = false,
            RejectMixedScripts = true,
        });

        ModerationDecision result = await pipeline.EvaluateAsync(Message("p\u0430ypal"));

        Assert.Equal(ModerationReason.MixedScripts, result.Reason);
    }

    [Fact]
    public async Task HoldsWhenRequiredClassifierIsUnavailable()
    {
        ModerationPipeline pipeline = CreatePipeline(new ModerationOptions
        {
            EnableClassifier = true,
            HoldWhenClassifierUnavailable = true,
        });

        ModerationDecision result = await pipeline.EvaluateAsync(Message("ordinary message"));

        Assert.Equal(ModerationDisposition.Held, result.Disposition);
        Assert.Equal(ModerationReason.ClassifierUnavailable, result.Reason);
    }

    [Fact]
    public async Task RejectsScoreAtConfiguredToxicityThreshold()
    {
        var classifier = new FakeClassifier(0.91);
        var pipeline = new ModerationPipeline(
            new Blocklist(),
            classifier,
            new ModerationOptions { EnableClassifier = true, ToxicityThreshold = 0.85 });

        ModerationDecision result = await pipeline.EvaluateAsync(Message("synthetic classifier input"));

        Assert.Equal(ModerationReason.Toxicity, result.Reason);
        Assert.Equal(0.91, result.ToxicityScore);
    }

    private static ModerationPipeline CreatePipeline(ModerationOptions? options = null) =>
        new(new Blocklist(["badword"]), new UnavailableToxicityClassifier(), options);

    private static ChatMessage Message(string text) =>
        new("message-1", "viewer", "Viewer", text, AudienceRole.Guest, DateTimeOffset.UtcNow);

    private sealed class FakeClassifier(double score) : IToxicityClassifier
    {
        public bool IsAvailable => true;

        public ValueTask<double?> ScoreAsync(string text, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<double?>(score);
    }
}
