using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public sealed class MentionSanitizationTests
{
    [Fact]
    public async Task MixedScriptMention_IsSanitizedToAPlayer_AndMessageIsApproved()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = true,
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(config, intentClassifier: new FakeSafeClassifier());

        var message = new ChatMessage
        {
            Author = "rj_user",
            AuthorDisplayName = "R.J",
            RawText = "@⨂Alex𒉭 watch his pinned videos"
        };

        var decision = await pipeline.ProcessMessageAsync(message);

        Assert.True(decision.Passed);
        Assert.Equal(ModerationDisposition.Approved, decision.Disposition);
        Assert.Contains("a player watch his pinned videos", decision.SpokenText);
        Assert.DoesNotContain("⨂Alex𒉭", decision.SpokenText);
    }

    [Fact]
    public async Task NonLatinRunesAndSymbolsMention_IsSanitizedToAPlayer_AndMessageIsApproved()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = true,
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(config, intentClassifier: new FakeSafeClassifier());

        var message1 = new ChatMessage
        {
            Author = "riley",
            AuthorDisplayName = "Riley",
            RawText = "@burnitdownꑭ it’s superfan"
        };

        var decision1 = await pipeline.ProcessMessageAsync(message1);
        Assert.True(decision1.Passed);
        Assert.Contains("a player it’s superfan", decision1.SpokenText);

        var message2 = new ChatMessage
        {
            Author = "lee",
            AuthorDisplayName = "Lee",
            RawText = "@𝙅︻╦̵̵͇─▄︻̷̿┻̿═━一 you’re good just look at the messages"
        };

        var decision2 = await pipeline.ProcessMessageAsync(message2);
        Assert.True(decision2.Passed);
        Assert.Contains("a player you’re good just look at the messages", decision2.SpokenText);
    }

    [Fact]
    public async Task ToxicWordInUsernameMention_IsSanitizedToAPlayer_AndCleanMessagePasses()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = true,
            IntentModerationLevel = 3,
            UserCooldownSeconds = 0
        };

        var classifier = new FlaggingClassifier("neglectedpuppyboy", 0.75);
        using var pipeline = new ModerationPipeline(config, intentClassifier: classifier);

        var message = new ChatMessage
        {
            Author = "caleb",
            AuthorDisplayName = "caleb",
            RawText = "@neglectedpuppyboy Yes tomorrow prob"
        };

        var decision = await pipeline.ProcessMessageAsync(message);

        Assert.True(decision.Passed);
        Assert.Contains("a player Yes tomorrow prob", decision.SpokenText);
        Assert.DoesNotContain("neglectedpuppyboy", decision.SpokenText);
    }

    [Fact]
    public async Task CleanStandardMention_IsPreservedInSpeech()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = true,
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(config, intentClassifier: new FakeSafeClassifier());

        var message = new ChatMessage
        {
            Author = "stan",
            AuthorDisplayName = "Stan",
            RawText = "@Tglamb08 what language is that???"
        };

        var decision = await pipeline.ProcessMessageAsync(message);

        Assert.True(decision.Passed);
        Assert.Contains("@Tglamb08", decision.SpokenText);
    }

    [Fact]
    public async Task HostileMessageWithMention_IsRejected()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = true,
            IntentModerationLevel = 3,
            UserCooldownSeconds = 0
        };

        var classifier = new FlaggingClassifier("sucks tho", 0.95);
        using var pipeline = new ModerationPipeline(config, intentClassifier: classifier);

        var message = new ChatMessage
        {
            Author = "viewer",
            AuthorDisplayName = "Viewer",
            RawText = "@Tglamb08 sucks tho"
        };

        var decision = await pipeline.ProcessMessageAsync(message);

        Assert.False(decision.Passed);
        Assert.Equal(ModerationDisposition.Rejected, decision.Disposition);
    }

    [Fact]
    public async Task ProhibitedTermInMention_IsRejectedImmediately()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = true,
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(config, intentClassifier: new FakeSafeClassifier());

        var message = new ChatMessage
        {
            Author = "troll",
            AuthorDisplayName = "Troll",
            RawText = "@nigger"
        };

        var decision = await pipeline.ProcessMessageAsync(message);

        Assert.False(decision.Passed);
        Assert.Equal(ModerationDisposition.Rejected, decision.Disposition);
        Assert.Equal(ModerationReasonCode.BlockedTerm, decision.ReasonCode);
    }

    [Fact]
    public async Task SoleUnsafeMentionWithNoSubstantiveContent_IsRejectedAsSpam()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = true,
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(config, intentClassifier: new FakeSafeClassifier());

        var message = new ChatMessage
        {
            Author = "viewer",
            AuthorDisplayName = "Viewer",
            RawText = "@⨂Alex𒉭"
        };

        var decision = await pipeline.ProcessMessageAsync(message);

        Assert.False(decision.Passed);
        Assert.Equal(ModerationDisposition.Rejected, decision.Disposition);
        Assert.Equal(ModerationReasonCode.SpamPattern, decision.ReasonCode);
    }

    [Fact]
    public async Task MultipleMentions_AreEachResolvedIndependently()
    {
        var config = new ModerationConfig
        {
            EnglishOnly = true,
            RejectMixedScripts = true,
            UserCooldownSeconds = 0
        };

        using var pipeline = new ModerationPipeline(config, intentClassifier: new FakeSafeClassifier());

        var message = new ChatMessage
        {
            Author = "viewer",
            AuthorDisplayName = "Viewer",
            RawText = "@⨂Alex𒉭 and @burnitdownꑭ check out the stream"
        };

        var decision = await pipeline.ProcessMessageAsync(message);

        Assert.True(decision.Passed);
        Assert.Contains("a player and a player check out the stream", decision.SpokenText);
    }

    private sealed class FakeSafeClassifier : IIntentClassifier
    {
        public string ModelName => "FakeSafe";
        public bool IsModelLoaded => true;
        public Task<IntentClassificationResult> ClassifyAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new IntentClassificationResult { ToxicityScore = 0.01 });
        public void Dispose() { }
    }

    private sealed class FlaggingClassifier : IIntentClassifier
    {
        private readonly string _flagTrigger;
        private readonly double _score;

        public FlaggingClassifier(string flagTrigger, double score)
        {
            _flagTrigger = flagTrigger;
            _score = score;
        }

        public string ModelName => "FlaggingClassifier";
        public bool IsModelLoaded => true;
        public Task<IntentClassificationResult> ClassifyAsync(string text, CancellationToken cancellationToken = default)
        {
            bool match = text.Contains(_flagTrigger, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new IntentClassificationResult
            {
                ToxicityScore = match ? _score : 0.01,
                ThreatScore = match ? _score : 0.0,
                HarassmentScore = match ? _score : 0.0,
                FlaggedCategory = match ? "Toxic language" : "None"
            });
        }
        public void Dispose() { }
    }
}