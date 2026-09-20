using SafeSpeak.Core.AI;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public sealed class ContextualTargetingPolicyTests
{
    private readonly IntentClassificationResult _defaultResult = new()
    {
        IsToxic = false,
        ToxicityScore = 0.1,
        SevereToxicityScore = 0.05,
        ObsceneScore = 0.1,
        ThreatScore = 0.0,
        HarassmentScore = 0.0,
        InsultScore = 0.0,
        IdentityHateScore = 0.0,
        FlaggedCategory = "None",
        ModelUsed = "TestModel"
    };

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", null)]
    [InlineData(null, "   ")]
    public void Apply_ReturnsOriginalResult_WhenInputsAreNullOrWhitespace(string? original, string? normalized)
    {
        var result = ContextualTargetingPolicy.Apply(original!, normalized!, _defaultResult);

        Assert.Equal(_defaultResult, result);
    }

    [Theory]
    [InlineData("I want to have sex with a 12 year old")]
    [InlineData("send me your nudes, child")]
    public void Apply_FlagsExplicitMinorSexualIntent(string text)
    {
        var result = ContextualTargetingPolicy.Apply(text, text, _defaultResult);

        Assert.True(result.IsToxic);
        Assert.True(result.ToxicityScore >= 0.99);
        Assert.True(result.SevereToxicityScore >= 0.99);
        Assert.True(result.HarassmentScore >= 0.95);
        Assert.Equal("Explicit minor sexual exploitation", result.FlaggedCategory);
        Assert.Equal("TestModel + target-context policy", result.ModelUsed);
    }

    [Theory]
    [InlineData("I support the genocide")]
    [InlineData("you deserve a genocide")]
    [InlineData("we should exterminate them all")]
    public void Apply_FlagsExplicitGenocidalIntent(string text)
    {
        var result = ContextualTargetingPolicy.Apply(text, text, _defaultResult);

        Assert.True(result.IsToxic);
        Assert.True(result.ToxicityScore >= 0.99);
        Assert.True(result.SevereToxicityScore >= 0.99);
        Assert.True(result.ThreatScore >= 0.95);
        Assert.True(result.HarassmentScore >= 0.95);
        Assert.Equal("Genocidal advocacy or threat", result.FlaggedCategory);
        Assert.Equal("TestModel + target-context policy", result.ModelUsed);
    }

    [Theory]
    [InlineData("genocide is a crime against humanity")]
    [InlineData("we must prevent genocide")]
    [InlineData("remember the victims of genocide")]
    public void Apply_CapsProtectiveOrInformationalGenocideStatement(string text)
    {
        var result = ContextualTargetingPolicy.Apply(text, text, _defaultResult with { ToxicityScore = 0.9, SevereToxicityScore = 0.8 });

        Assert.False(result.IsToxic);
        Assert.True(result.ToxicityScore <= 0.40);
        Assert.True(result.SevereToxicityScore <= 0.40);
        Assert.Equal("Protective or informational genocide discussion", result.FlaggedCategory);
        Assert.Equal("TestModel + target-context policy", result.ModelUsed);
    }

    [Theory]
    [InlineData("child sexual abuse is wrong")]
    [InlineData("we must protect kids from sexual grooming")]
    [InlineData("I reported suspected child exploitation to the authorities")]
    public void Apply_CapsProtectiveMinorSafetyStatement(string text)
    {
        var result = ContextualTargetingPolicy.Apply(text, text, _defaultResult with { ToxicityScore = 0.8, SevereToxicityScore = 0.7 });

        Assert.False(result.IsToxic);
        Assert.True(result.ToxicityScore <= 0.40);
        Assert.True(result.SevereToxicityScore <= 0.40);
        Assert.Equal("Protective child-safety statement", result.FlaggedCategory);
        Assert.Equal("TestModel + target-context policy", result.ModelUsed);
    }

    [Theory]
    [InlineData("I am really looking forward to do private things with my niece")]
    [InlineData("my child and I have a special secret that adults shouldn't know")]
    public void Apply_FlagsAmbiguousMinorSafetyConcern(string text)
    {
        var result = ContextualTargetingPolicy.Apply(text, text, _defaultResult);

        Assert.True(result.IsToxic);
        Assert.Equal(0.70, result.ToxicityScore);
        Assert.True(result.SevereToxicityScore >= 0.60);
        Assert.True(result.HarassmentScore >= 0.65);
        Assert.Equal("Ambiguous child-safety concern", result.FlaggedCategory);
        Assert.Equal("TestModel + target-context policy", result.ModelUsed);
    }

    [Theory]
    [InlineData("I absolutely hate you")]
    [InlineData("this streamer is trash")]
    [InlineData("everyone in this chat is pathetic")]
    public void Apply_FlagsDirectedHostility_WithNoIndependentSevereSignal(string text)
    {
        var result = ContextualTargetingPolicy.Apply(text, text, _defaultResult);

        Assert.True(result.IsToxic);
        Assert.Equal(0.85, result.ToxicityScore);
        Assert.True(result.HarassmentScore >= 0.80);
        Assert.True(result.InsultScore >= 0.80);
        Assert.Equal("Directed hostility", result.FlaggedCategory);
        Assert.Equal("TestModel + target-context policy", result.ModelUsed);
    }

    [Fact]
    public void Apply_FlagsDirectedHostility_WithIndependentSevereSignal()
    {
        var severeResult = _defaultResult with { ThreatScore = 0.95, ToxicityScore = 0.90 };
        var text = "I absolutely hate you";
        var result = ContextualTargetingPolicy.Apply(text, text, severeResult);

        Assert.True(result.IsToxic);
        Assert.True(result.ToxicityScore >= 0.90);
        Assert.True(result.HarassmentScore >= 0.80);
        Assert.True(result.InsultScore >= 0.80);
        Assert.Equal("Directed hostility", result.FlaggedCategory);
        Assert.Equal("TestModel + target-context policy", result.ModelUsed);
    }

    [Theory]
    [InlineData("I really hate this game")]
    [InlineData("this level is garbage")]
    [InlineData("I hate losing this match")]
    public void Apply_CapsNonHumanFrustration_WhenUnderThreshold(string text)
    {
        var result = ContextualTargetingPolicy.Apply(text, text, _defaultResult with { ToxicityScore = 0.8 });

        Assert.False(result.IsToxic);
        Assert.True(result.ToxicityScore <= 0.40);
        Assert.Equal("Non-directed game frustration", result.FlaggedCategory);
        Assert.Equal("TestModel + target-context policy", result.ModelUsed);
    }

    [Fact]
    public void Apply_BypassesNonHumanFrustration_WhenOverThreshold()
    {
        var severeResult = _defaultResult with { ThreatScore = 0.80, ToxicityScore = 0.90 };
        var text = "I really hate this game";
        var result = ContextualTargetingPolicy.Apply(text, text, severeResult);

        Assert.Equal(severeResult.ToxicityScore, result.ToxicityScore);
        Assert.Equal(severeResult.ThreatScore, result.ThreatScore);
        Assert.Equal("None", result.FlaggedCategory);
    }
}
