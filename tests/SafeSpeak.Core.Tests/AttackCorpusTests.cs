using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

[Collection("Local moderation runtime")]
public class AttackCorpusTests : IDisposable
{
    private readonly ModerationPipeline _pipeline;

    public AttackCorpusTests()
    {
        var config = new ModerationConfig
        {
            AudienceMode = AudienceMode.All,
            Strictness = ModerationStrictness.High,
            RejectMixedScripts = true,
            EnglishOnly = true,
            AiClassificationEnabled = true,
            UserCooldownSeconds = 0 // Disable for attack corpus testing
        };

        _pipeline = new ModerationPipeline(config);
    }

    [Theory]
    // Clean messages
    [InlineData("Hello everyone, how are you doing today?", true)]
    [InlineData("Great shot! What a game!", true)]
    [InlineData("Thank you for streaming, love the content", true)]
    // Zero-width evasion attacks on slurs
    [InlineData("You are a f\u200Ba\u200Bg\u200Bg\u200Bo\u200Bt", false)]
    [InlineData("f\uFEFFa\uFEFFg\uFEFFg\uFEFFo\uFEFFt", false)]
    // Cyrillic / Homoglyph lookalike attacks
    [InlineData("You are a f\u0430ggot", false)] // Cyrillic 'а'
    [InlineData("go k\u0443s right now", false)] // Cyrillic 'у'
    // Spaced letter evasion
    [InlineData("k y s right now", false)]
    [InlineData("f a g g o t", false)]
    // Leetspeak / symbol substitutions
    [InlineData("k1ll your$elf", false)]
    [InlineData("die in a f1re", false)]
    // Repetition stretch
    [InlineData("kyyyyysssss", false)]
    public async Task ProcessMessage_CorrectlyFiltersAttacksAndAllowsCleanMessages(string text, bool shouldPass)
    {
        var message = new ChatMessage
        {
            Author = "test_viewer",
            AuthorDisplayName = "Test Viewer",
            RawText = text,
            AuthorTier = AuthorTier.Viewer
        };

        var decision = await _pipeline.ProcessMessageAsync(message);

        Assert.Equal(shouldPass, decision.Passed);
        if (!shouldPass)
        {
            Assert.NotEqual(ModerationReasonCode.None, decision.ReasonCode);
        }
    }

    [Fact]
    public async Task ProcessMessage_StripsUrlsFromSpokenTextWhenCleanMessageApproved()
    {
        var message = new ChatMessage
        {
            Author = "fan_user",
            RawText = "Check out my music at https://spotify.com/track/12345 it is awesome"
        };

        var decision = await _pipeline.ProcessMessageAsync(message);

        Assert.True(decision.Passed);
        Assert.DoesNotContain("https://", decision.SpokenText);
        Assert.Contains("[link removed]", decision.SpokenText);
    }

    [Fact]
    public async Task ProcessMessage_HonorsAudienceTierRequirements()
    {
        using var subOnlyPipeline = new ModerationPipeline(new ModerationConfig
        {
            AudienceMode = AudienceMode.SubscribersOnly,
            UserCooldownSeconds = 0
        });

        var viewerMessage = new ChatMessage
        {
            Author = "casual_viewer",
            RawText = "Hello streamer",
            AuthorTier = AuthorTier.Viewer
        };

        var subMessage = new ChatMessage
        {
            Author = "tier1_sub",
            RawText = "Hello streamer",
            AuthorTier = AuthorTier.Subscriber
        };

        var viewerDecision = await subOnlyPipeline.ProcessMessageAsync(viewerMessage);
        var subDecision = await subOnlyPipeline.ProcessMessageAsync(subMessage);

        Assert.False(viewerDecision.Passed);
        Assert.Equal(ModerationReasonCode.AudienceRestricted, viewerDecision.ReasonCode);

        Assert.True(subDecision.Passed);
    }

    [Fact]
    public async Task ProcessMessage_CanAllowCurrentStreamDonorsThroughAudienceRestriction()
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            AudienceMode = AudienceMode.SubscribersOnly,
            AllowDonorsToSpeak = true,
            UserCooldownSeconds = 0
        });
        var donorMessage = new ChatMessage
        {
            Author = "gift_sender",
            RawText = "Thank you for the stream",
            AuthorTier = AuthorTier.Viewer,
            IsDonor = true
        };

        ModerationDecision allowed = await pipeline.ProcessMessageAsync(donorMessage);

        Assert.True(allowed.Passed);

        pipeline.Config.AllowDonorsToSpeak = false;
        ModerationDecision blocked = await pipeline.ProcessMessageAsync(
            donorMessage with { Id = Guid.NewGuid().ToString("N") });
        Assert.Equal(ModerationReasonCode.AudienceRestricted, blocked.ReasonCode);
    }

    [Fact]
    public async Task ProcessMessage_ReplacesUnsafeDisplayNameBeforeSpeech()
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            SpeakUsernames = true,
            UserCooldownSeconds = 0
        });

        var decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer-id",
            AuthorDisplayName = "k y s",
            RawText = "Hello streamer"
        });

        Assert.True(decision.Passed);
        Assert.Equal("A viewer says: Hello streamer", decision.SpokenText);
        Assert.Equal("A viewer", decision.SafeAuthorDisplayName);
        Assert.DoesNotContain("k y s", decision.AccessibleSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectedDecision_AccessibleSummaryDoesNotExposeHostileContent()
    {
        var decision = await _pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "hostile-id",
            AuthorDisplayName = "hostile name",
            RawText = "kill yourself"
        });

        Assert.False(decision.Passed);
        Assert.Equal("Content hidden for safety.", decision.SafeDisplayText);
        Assert.DoesNotContain("kill yourself", decision.AccessibleSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hostile", decision.AccessibleSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomAllowedTerm_DoesNotOverrideBuiltInSevereRule()
    {
        var config = new ModerationConfig { UserCooldownSeconds = 0 };
        config.CustomAllowedTerms.Add("friendly phrase");
        using var pipeline = new ModerationPipeline(config);

        var decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer-id",
            RawText = "friendly phrase and kill yourself"
        });

        Assert.False(decision.Passed);
        Assert.Equal(ModerationReasonCode.BlockedTerm, decision.ReasonCode);
    }

    [Fact]
    public async Task CustomAllowedTerm_CanOverrideMatchingCustomBlockOnly()
    {
        var config = new ModerationConfig { UserCooldownSeconds = 0 };
        config.CustomBlockedTerms.Add("pineapple");
        config.CustomAllowedTerms.Add("pineapple");
        using var pipeline = new ModerationPipeline(config);

        var decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer-id",
            RawText = "pineapple is allowed"
        });

        Assert.True(decision.Passed);
    }

    public void Dispose() => _pipeline.Dispose();
}
