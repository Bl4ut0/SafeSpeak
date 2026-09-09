using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public sealed class DisallowedScriptModerationPipelineTests
{
    [Fact]
    public async Task ProcessMessageAsync_RejectsReportedCjkMessage_UnderDefaultProtection()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = true,
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(
            config,
            intentClassifier: new FakeClassifier());

        string reportedMessage = ".1, 2, 3 ㌕㌖㌖㌕㌖㌖㌕㌖㌖㌕㌖㌖1, 2, 3 ㌕㌖";

        var chatMessage = new ChatMessage
        {
            Author = "meraj_hmb",
            AuthorDisplayName = "Meraj_hh🇦🇫",
            AuthorTier = AuthorTier.Follower,
            RawText = reportedMessage
        };

        var decision = await pipeline.ProcessMessageAsync(chatMessage);

        Assert.Equal(ModerationDisposition.Rejected, decision.Disposition);
        Assert.Equal(ModerationReasonCode.DisallowedScript, decision.ReasonCode);
        Assert.Empty(decision.SpokenText);
    }

    [Fact]
    public async Task ProcessMessageAsync_RejectsPureCjkSquareCharacters_WhenEnglishOnlyWithoutMixedScriptFilter()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = false,
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(
            config,
            intentClassifier: new FakeClassifier());

        var chatMessage = new ChatMessage
        {
            Author = "viewer",
            AuthorDisplayName = "Viewer",
            RawText = "㌕㌖㌖㌕㌖"
        };

        var decision = await pipeline.ProcessMessageAsync(chatMessage);

        Assert.Equal(ModerationDisposition.Rejected, decision.Disposition);
        Assert.Equal(ModerationReasonCode.DisallowedScript, decision.ReasonCode);
        Assert.Contains("Non-Latin script detected", decision.ReasonDescription);
        Assert.Empty(decision.SpokenText);
    }

    [Fact]
    public async Task ProcessMessageAsync_SanitizesDisplayNameWithNonLatinScript_WhenEnglishOnly()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = true,
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(
            config,
            intentClassifier: new FakeClassifier());

        var chatMessage = new ChatMessage
        {
            Author = "user1",
            AuthorDisplayName = "こんにちは",
            RawText = "Hello streamer"
        };

        var decision = await pipeline.ProcessMessageAsync(chatMessage);

        Assert.Equal(ModerationDisposition.Approved, decision.Disposition);
        Assert.Equal("A viewer", decision.SafeAuthorDisplayName);
        Assert.Contains("A viewer says: Hello streamer", decision.SpokenText);
    }

    [Fact]
    public async Task ProcessMessageAsync_RejectsCompatibilityGreekBeforeHomoglyphMapping()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = false,
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(
            config,
            intentClassifier: new FakeClassifier());

        var decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer",
            AuthorDisplayName = "Viewer",
            RawText = "Compatibility Greek: Ω"
        });

        Assert.Equal(ModerationDisposition.Rejected, decision.Disposition);
        Assert.Equal(ModerationReasonCode.DisallowedScript, decision.ReasonCode);
        Assert.Empty(decision.SpokenText);
    }

    private sealed class FakeClassifier : IIntentClassifier
    {
        public string ModelName => "Fake";
        public bool IsModelLoaded => true;
        public Task<IntentClassificationResult> ClassifyAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new IntentClassificationResult { ToxicityScore = 0.01 });
        public void Dispose() { }
    }
}
