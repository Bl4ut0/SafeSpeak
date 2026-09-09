using System.Net;
using System.Text;
using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class Qwen3GuardIntentClassifierTests
{
    [Fact]
    public async Task UnsafeNativeResponse_MapsToBlockingScores()
    {
        using var client = new HttpClient(new StaticResponseHandler(
            """
            {"message":{"content":"Safety: Unsafe\nCategories: Violent"}}
            """));
        using var classifier = new Qwen3GuardIntentClassifier(
            fallback: new SafeFallback(),
            httpClient: client);

        IntentClassificationResult result =
            await classifier.ClassifyAsync("A newly worded violent threat");

        Assert.True(result.IsToxic);
        Assert.Equal(0.95, result.ToxicityScore);
        Assert.Equal(0.95, result.ThreatScore);
        Assert.True(classifier.IsModelLoaded);
        Assert.Contains("Qwen3Guard Violent", result.FlaggedCategory);
    }

    [Fact]
    public async Task ControversialNativeResponse_MapsToSliderControlledScore()
    {
        using var client = new HttpClient(new StaticResponseHandler(
            """
            {"message":{"content":"Safety: Controversial\nCategories: Harassment"}}
            """));
        using var classifier = new Qwen3GuardIntentClassifier(
            fallback: new SafeFallback(),
            httpClient: client);

        IntentClassificationResult result =
            await classifier.ClassifyAsync("Borderline directed wording");

        Assert.False(result.IsToxic);
        Assert.Equal(0.55, result.ToxicityScore);
        Assert.Equal(0.55, result.HarassmentScore);
        Assert.True(result.ToxicityScore < ModerationConfig.GetIntentToxicityThreshold(3));
        Assert.True(result.ToxicityScore >= ModerationConfig.GetIntentToxicityThreshold(4));
    }

    [Theory]
    [InlineData(". jaymo's eye is radioactive", "Violent")]
    [InlineData("Love the radioactive eyepatch!!", "Sexual Content or Sexual Acts")]
    [InlineData(". jaymo would you hate me if i sent you a change eye patch right now?", "Unethical Acts")]
    [InlineData(".do you know how to backflip", "Violent")]
    [InlineData(".i keep crashing my cars", "Unethical Acts")]
    [InlineData("evil Morty reference", "Politically Sensitive Topics")]
    public async Task ControversialAuditExamplesRemainBelowStrongThreshold(
        string text,
        string category)
    {
        using var client = new HttpClient(new StaticResponseHandler(
            $"{{\"message\":{{\"content\":\"Safety: Controversial\\nCategories: {category}\"}}}}"));
        using var classifier = new Qwen3GuardIntentClassifier(
            fallback: new SafeFallback(),
            httpClient: client);

        IntentClassificationResult result = await classifier.ClassifyAsync(text);

        Assert.Equal(0.55, result.ToxicityScore);
        Assert.True(result.ToxicityScore < ModerationConfig.GetIntentToxicityThreshold(3));
        Assert.True(result.ToxicityScore >= ModerationConfig.GetIntentToxicityThreshold(4));
    }

    [Fact]
    public async Task UnavailableEndpoint_UsesBundledFallback()
    {
        using var client = new HttpClient(new StaticResponseHandler(
            "{}",
            HttpStatusCode.ServiceUnavailable));
        using var classifier = new Qwen3GuardIntentClassifier(
            fallback: new FixedFallback(0.42),
            httpClient: client);

        IntentClassificationResult result =
            await classifier.ClassifyAsync("ordinary message");

        Assert.Equal(0.42, result.ToxicityScore);
        Assert.False(classifier.IsModelLoaded);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://192.168.1.25:11434")]
    public void Constructor_RejectsNonLoopbackEndpoints(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => new Qwen3GuardIntentClassifier(
            endpoint,
            fallback: new SafeFallback()));
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _statusCode;

        public StaticResponseHandler(
            string json,
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _json = json;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
    }

    private class SafeFallback : IIntentClassifier
    {
        public string ModelName => "Safe test fallback";
        public bool IsModelLoaded => true;
        public virtual Task<IntentClassificationResult> ClassifyAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IntentClassificationResult());
        public void Dispose() { }
    }

    private sealed class FixedFallback(double score) : SafeFallback
    {
        public override Task<IntentClassificationResult> ClassifyAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IntentClassificationResult
            {
                ToxicityScore = score,
                IsToxic = score >= 0.60,
                FlaggedCategory = "Fallback"
            });
    }
}
