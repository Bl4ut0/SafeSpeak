using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

[Collection("Local moderation runtime")]
public sealed class IntentModerationLevelTests
{
    [Theory]
    [InlineData(1, "shut up already", true)]
    [InlineData(2, "shut up already", false)]
    [InlineData(3, "shut up already", false)]
    [InlineData(4, "shut up already", false)]
    [InlineData(1, "you are annoying", true)]
    [InlineData(2, "you are annoying", true)]
    [InlineData(3, "you are annoying", true)]
    [InlineData(4, "you are annoying", false)]
    public async Task ModerationLevelChangesUncertainHostilityDecision(
        int level,
        string text,
        bool shouldPass)
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = level,
            UserCooldownSeconds = 0
        });

        var decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer",
            RawText = text
        });

        Assert.True(
            decision.Passed == shouldPass,
            $"Expected pass={shouldPass}, actual pass={decision.Passed}, score={decision.ToxicityScore:F4}, reason={decision.ReasonDescription}");
    }

    [Theory]
    [InlineData("fuck off")]
    [InlineData("fuuuuuck off")]
    [InlineData("f u c k off")]
    [InlineData("f\u200Buck off")]
    [InlineData("go fuck yourself")]
    public async Task StrongModerationRejectsHostileProfanityAndEvasions(string text)
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = 3,
            UserCooldownSeconds = 0
        });

        ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer",
            AuthorDisplayName = "Friendly Viewer",
            RawText = text
        });

        Assert.False(decision.Passed);
        Assert.True(
            decision.ReasonCode is ModerationReasonCode.SevereToxicity or
                ModerationReasonCode.ThreatOrHarassment or
                ModerationReasonCode.DisallowedScript);
        Assert.Equal(string.Empty, decision.SpokenText);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task BannedTermsRemainBlockedAtEveryModerationLevel(int level)
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = level,
            UserCooldownSeconds = 0,
            CustomBlockedTerms = ["pineapple"]
        });

        var decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer",
            RawText = "pineapple"
        });

        Assert.False(decision.Passed);
        Assert.Equal(ModerationReasonCode.BlockedTerm, decision.ReasonCode);
    }

    [Theory]
    [InlineData("I hope something terrible happens to you today.")]
    [InlineData("i hope something horrible happens to you")]
    [InlineData("hope you get into a car accident")]
    [InlineData("I hope you get cancer and suffer")]
    [InlineData("hope you choke")]
    [InlineData("you deserve to suffer")]
    [InlineData("may you rot in hell")]
    public async Task MaliciousHarmWishesAreBlockedAtBalancedStrongAndMaximumLevels(string text)
    {
        foreach (int level in new[] { 2, 3, 4 })
        {
            using var pipeline = new ModerationPipeline(new ModerationConfig
            {
                IntentModerationLevel = level,
                UserCooldownSeconds = 0
            });

            var decision = await pipeline.ProcessMessageAsync(new ChatMessage
            {
                Author = "hostile_viewer",
                RawText = text
            });

            Assert.False(decision.Passed, $"Expected '{text}' to be blocked at level {level}, but it passed.");
            Assert.True(
                decision.ReasonCode is ModerationReasonCode.ThreatOrHarassment or ModerationReasonCode.SevereToxicity,
                $"Expected ThreatOrHarassment reason code, got {decision.ReasonCode}");
            Assert.Empty(decision.SpokenText);
        }
    }

    [Theory]
    [InlineData("watch your back")]
    [InlineData("I know where you live")]
    [InlineData("we are coming for you")]
    [InlineData("you're not safe")]
    [InlineData("better lock your doors tonight")]
    [InlineData("someone is going to hurt you")]
    [InlineData("the world would be better off without you")]
    [InlineData("nobody would care if you died")]
    [InlineData("drink bleach")]
    [InlineData("waste of oxygen")]
    public async Task VeiledThreatsAndSevereDegradationAreBlocked(string text)
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = 3, // Strong (Default)
            UserCooldownSeconds = 0
        });

        var decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "hostile_viewer",
            RawText = text
        });

        Assert.False(decision.Passed, $"Expected '{text}' to be blocked, but it passed.");
        Assert.Empty(decision.SpokenText);
    }

    [Theory]
    [InlineData("I hope you have an amazing day today!")]
    [InlineData("I hope this stream goes really well!")]
    [InlineData("Hope you win your next match!")]
    [InlineData("Wish you all the best streamer!")]
    [InlineData("Great stream today, thank you!")]
    public async Task CleanPositiveWishesPassAtAllModerationLevels(string text)
    {
        for (int level = 1; level <= 4; level++)
        {
            using var pipeline = new ModerationPipeline(new ModerationConfig
            {
                IntentModerationLevel = level,
                UserCooldownSeconds = 0
            });

            var decision = await pipeline.ProcessMessageAsync(new ChatMessage
            {
                Author = "friendly_viewer",
                RawText = text
            });

            Assert.True(decision.Passed, $"Expected '{text}' to pass at level {level}, but it was rejected with {decision.ReasonDescription}");
            Assert.NotEmpty(decision.SpokenText);
        }
    }

    [Theory]
    [InlineData("I hate this game.")]
    [InlineData("This game is trash.")]
    [InlineData("This boss is stupid.")]
    [InlineData("This update is awful.")]
    [InlineData("I hate losing this round.")]
    public async Task NonHumanGameFrustrationPassesAtEveryModerationLevel(string text)
    {
        for (int level = 1; level <= 4; level++)
        {
            using var pipeline = new ModerationPipeline(new ModerationConfig
            {
                IntentModerationLevel = level,
                UserCooldownSeconds = 0
            });

            ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
            {
                Author = "frustrated_viewer",
                RawText = text
            });

            Assert.True(
                decision.Passed,
                $"Expected non-human frustration '{text}' to pass at level {level}, " +
                $"but it scored {decision.ToxicityScore:F4}: {decision.ReasonDescription}");
        }
    }

    [Theory]
    [InlineData("I hate you.")]
    [InlineData("I hate this stream.")]
    [InlineData("I hate this streamer.")]
    [InlineData("I hate @alex.")]
    [InlineData("I hate everyone.")]
    [InlineData("This streamer is trash.")]
    [InlineData("Everyone in this chat is trash.")]
    public async Task PersonStreamAndGroupDirectedHostilityFollowsPermissiveToBalancedBoundary(string text)
    {
        for (int level = 1; level <= 4; level++)
        {
            using var pipeline = new ModerationPipeline(new ModerationConfig
            {
                IntentModerationLevel = level,
                UserCooldownSeconds = 0
            });

            ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
            {
                Author = "hostile_viewer",
                RawText = text
            });

            bool shouldPass = level == 1;
            Assert.True(
                decision.Passed == shouldPass,
                $"Expected directed hostility '{text}' pass={shouldPass} at level {level}. " +
                $"Actual score: {decision.ToxicityScore:F4}.");
            if (!shouldPass)
            {
                Assert.Equal(ModerationReasonCode.ThreatOrHarassment, decision.ReasonCode);
            }
        }
    }

    [Theory]
    [InlineData("I want to have sex with my underage niece.")]
    [InlineData("I plan to do sexual things with a child.")]
    [InlineData("That underage girl is sexy.")]
    [InlineData("I can't wait until my niece is legal.")]
    public async Task ExplicitMinorSexualIntentIsBlockedAtEveryLevel(string text)
    {
        for (int level = 1; level <= 4; level++)
        {
            using var pipeline = new ModerationPipeline(new ModerationConfig
            {
                IntentModerationLevel = level,
                UserCooldownSeconds = 0
            });

            ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
            {
                Author = "viewer",
                RawText = text
            });

            Assert.False(decision.Passed, $"Expected explicit minor-safety case to be blocked at level {level}.");
            Assert.Empty(decision.SpokenText);
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public async Task AmbiguousFamilyEuphemismUsesSliderBoundary(int level, bool shouldPass)
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = level,
            UserCooldownSeconds = 0
        });

        ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer",
            RawText = "I'm excited to do things with my niece."
        });

        Assert.Equal(shouldPass, decision.Passed);
        Assert.Equal(0.70, decision.ToxicityScore, precision: 2);
        if (!shouldPass)
        {
            Assert.Empty(decision.SpokenText);
        }
    }

    [Theory]
    [InlineData("I'm excited to play video games with my niece.")]
    [InlineData("Sexual abuse of children is wrong.")]
    [InlineData("Protect children from sexual exploitation.")]
    [InlineData("I reported suspected child abuse to the authorities.")]
    public async Task BenignAndProtectiveChildSafetyContextPassesEveryLevel(string text)
    {
        for (int level = 1; level <= 4; level++)
        {
            using var pipeline = new ModerationPipeline(new ModerationConfig
            {
                IntentModerationLevel = level,
                UserCooldownSeconds = 0
            });

            ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
            {
                Author = "viewer",
                RawText = text
            });

            Assert.True(
                decision.Passed,
                $"Expected protective or benign context '{text}' to pass at level {level}: {decision.ReasonDescription}");
        }
    }

    [Theory]
    [InlineData("Sexual abuse of children is wrong, but I want to have sex with an underage person.")]
    [InlineData("Protect children from exploitation, and send me nude pictures, little girl.")]
    public async Task ProtectiveWordingCannotHideAppendedMinorSexualIntent(string text)
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = 1,
            UserCooldownSeconds = 0
        });

        ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer",
            RawText = text
        });

        Assert.False(decision.Passed);
        Assert.Empty(decision.SpokenText);
    }

    [Theory]
    [InlineData("I hate this game, and I know where you live.")]
    [InlineData("I hate this game and everyone in this chat is trash.")]
    [InlineData("I hate this game. Kill yourself.")]
    public async Task NonHumanFrustrationPolicyNeverOverridesAdditionalDirectedAbuse(string text)
    {
        using var pipeline = new ModerationPipeline(new ModerationConfig
        {
            IntentModerationLevel = 4,
            UserCooldownSeconds = 0
        });

        ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "hostile_viewer",
            RawText = text
        });

        Assert.False(decision.Passed);
        Assert.Empty(decision.SpokenText);
    }
}
