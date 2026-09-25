using System.Threading.Tasks;
using SafeSpeak.Core.AI;
using Xunit;

namespace SafeSpeak.Core.Tests;

public class HeuristicIntentClassifierTests
{
    private readonly HeuristicIntentClassifier _classifier = new();

    [Fact]
    public void ModelName_IsCorrect()
    {
        Assert.Equal("FastHeuristicIntentEngine (Built-in)", _classifier.ModelName);
    }

    [Fact]
    public void IsModelLoaded_IsTrue()
    {
        Assert.True(_classifier.IsModelLoaded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ClassifyAsync_EmptyText_ReturnsNotToxic(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.False(result.IsToxic);
        Assert.Equal(0.0, result.ToxicityScore);
    }

    [Theory]
    [InlineData("I hope you have a great day")]
    [InlineData("This game is amazing")]
    [InlineData("Hello everyone")]
    public async Task ClassifyAsync_CleanText_ReturnsNotToxic(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.False(result.IsToxic);
        Assert.Equal(0.0, result.ToxicityScore);
    }

    [Theory]
    [InlineData("kill yourself")]
    [InlineData("burn yourself")]
    [InlineData("shoot him")]
    public async Task ClassifyAsync_Threat_ReturnsHighThreatScore(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.True(result.IsToxic);
        Assert.True(result.ThreatScore >= 0.95);
        Assert.Equal("Threat or intimidation", result.FlaggedCategory);
    }

    [Theory]
    [InlineData("watch your back")]
    [InlineData("i know where you live")]
    [InlineData("we are coming to your house")]
    [InlineData("you better lock your doors")]
    public async Task ClassifyAsync_VeiledThreat_ReturnsHighThreatScore(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.True(result.IsToxic);
        Assert.True(result.ThreatScore >= 0.95);
        Assert.Equal("Threat or intimidation", result.FlaggedCategory);
    }

    [Theory]
    [InlineData("i hope you get cancer")]
    [InlineData("wish you die")]
    [InlineData("hope your family gets hurt")]
    public async Task ClassifyAsync_MaliciousHarmWish_ReturnsHighThreatScore(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.True(result.IsToxic);
        Assert.True(result.ThreatScore >= 0.85);
        Assert.Equal("Hostile harm wish", result.FlaggedCategory);
    }

    [Theory]
    [InlineData("do us a favor and kill yourself")]
    [InlineData("jump off a bridge")]
    [InlineData("end your life")]
    public async Task ClassifyAsync_SelfHarmEncouragement_ReturnsSevereToxicity(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.True(result.IsToxic);
        Assert.True(result.SevereToxicityScore >= 0.98);
        Assert.Equal("Self-harm encouragement", result.FlaggedCategory);
    }

    [Theory]
    [InlineData("waste of oxygen")]
    [InlineData("you are worthless")]
    [InlineData("nobody loves you")]
    public async Task ClassifyAsync_TargetedDegradation_ReturnsHighHarassmentScore(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.True(result.IsToxic);
        Assert.True(result.HarassmentScore >= 0.90);
        Assert.Equal("Targeted degradation", result.FlaggedCategory);
    }

    [Theory]
    [InlineData("i have your ip address")]
    [InlineData("swatting you")]
    [InlineData("leaking your location")]
    public async Task ClassifyAsync_DoxxSwatThreat_ReturnsHighThreatScore(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.True(result.IsToxic);
        Assert.True(result.ThreatScore >= 0.95);
        Assert.Equal("Threat or intimidation", result.FlaggedCategory);
    }

    [Theory]
    [InlineData("hope you fall off")]
    [InlineData("hope u get banned")]
    public async Task ClassifyAsync_SpitefulWish_ReturnsHarassmentScore(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.True(result.IsToxic);
        Assert.True(result.HarassmentScore >= 0.70);
        Assert.Equal("Hostile spite", result.FlaggedCategory);
    }

    [Theory]
    [InlineData("you are an idiot")]
    [InlineData("u r trash")]
    public async Task ClassifyAsync_DirectedInsult_ReturnsInsultScore(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.True(result.IsToxic);
        Assert.True(result.InsultScore >= 0.70);
        Assert.Equal("Directed insult", result.FlaggedCategory);
    }

    [Theory]
    [InlineData("idiot")]
    [InlineData("moron")]
    public async Task ClassifyAsync_SingleInsult_ReturnsLowerInsultScore(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.False(result.IsToxic); // Total toxicity < 0.6
        Assert.Equal(0.48, result.InsultScore);
        Assert.Equal("Insult", result.FlaggedCategory);
    }

    [Fact]
    public async Task ClassifyAsync_MultipleInsults_ReturnsHigherInsultScore()
    {
        var result = await _classifier.ClassifyAsync("idiot moron");
        Assert.True(result.IsToxic); // Total toxicity = 0.64
        Assert.Equal(0.64, result.InsultScore);
        Assert.Equal("Insult", result.FlaggedCategory);
    }

    [Theory]
    [InlineData("get cancer")]
    [InlineData("go away and die")]
    public async Task ClassifyAsync_Harassment_ReturnsHarassmentScore(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.True(result.IsToxic);
        Assert.True(result.HarassmentScore >= 0.85);
        Assert.Equal("Harassment", result.FlaggedCategory);
    }

    [Theory]
    [InlineData("shut up")]
    [InlineData("go away")]
    public async Task ClassifyAsync_HostileDismissal_ReturnsInsultScore(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.False(result.IsToxic);
        Assert.True(result.InsultScore >= 0.52);
        Assert.Equal("Hostile dismissal", result.FlaggedCategory);
    }

    [Theory]
    [InlineData("fuck off")]
    [InlineData("go fuck yourself")]
    public async Task ClassifyAsync_HostileProfanity_ReturnsInsultScore(string text)
    {
        var result = await _classifier.ClassifyAsync(text);
        Assert.True(result.IsToxic);
        Assert.True(result.InsultScore >= 0.82);
        Assert.Equal("Hostile profanity", result.FlaggedCategory);
    }
}
