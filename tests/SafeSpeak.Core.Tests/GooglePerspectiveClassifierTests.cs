using System.Net;
using System.Text;
using System.Text.Json;
using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public class GooglePerspectiveClassifierTests
{
    [Fact]
    public async Task ClassifyAsync_EmptyText_ReturnsZeroToxicity()
    {
        using var classifier = new GooglePerspectiveClassifier(apiKey: "fake_key");
        var result = await classifier.ClassifyAsync("");

        Assert.False(result.IsToxic);
        Assert.Equal(0.0, result.ToxicityScore);
    }

    [Fact]
    public async Task ClassifyAsync_NoApiKey_FallsBackToLocalClassifier()
    {
        using var classifier = new GooglePerspectiveClassifier(apiKey: null);
        var result = await classifier.ClassifyAsync("I hope something terrible happens to you today.");

        Assert.True(result.IsToxic);
        Assert.True(result.ToxicityScore >= 0.85);
    }

    [Fact]
    public async Task ClassifyAsync_SuccessfulPerspectiveResponse_FusesProperly()
    {
        var mockResponse = new
        {
            attributeScores = new Dictionary<string, object>
            {
                ["TOXICITY"] = new { summaryScore = new { value = 0.92 } },
                ["SEVERE_TOXICITY"] = new { summaryScore = new { value = 0.85 } },
                ["THREAT"] = new { summaryScore = new { value = 0.88 } },
                ["INSULT"] = new { summaryScore = new { value = 0.70 } },
                ["IDENTITY_ATTACK"] = new { summaryScore = new { value = 0.10 } },
                ["PROFANITY"] = new { summaryScore = new { value = 0.40 } }
            }
        };

        var handler = new MockHttpMessageHandler(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(mockResponse));

        using var httpClient = new HttpClient(handler);
        using var classifier = new GooglePerspectiveClassifier(
            apiKey: "valid_test_key",
            httpClient: httpClient);

        var result = await classifier.ClassifyAsync("You are going to regret this");

        Assert.True(result.IsToxic);
        Assert.Equal(0.92, result.ToxicityScore);
        Assert.Equal(0.88, result.ThreatScore);
        Assert.Equal("Threat", result.FlaggedCategory);
    }

    [Fact]
    public void IntentClassifierFactory_CreatesAppropriateClassifier()
    {
        var settings = new AppSettings
        {
            SelectedIntentEngineId = "local_hybrid"
        };
        using var localClassifier = IntentClassifierFactory.Create(settings);
        Assert.IsType<LocalOnnxIntentClassifier>(localClassifier);

        settings.SelectedIntentEngineId = "google_perspective";
        settings.PerspectiveApiKey = "test_key";
        using var perspectiveClassifier = IntentClassifierFactory.Create(settings);
        Assert.IsType<GooglePerspectiveClassifier>(perspectiveClassifier);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
