using SafeSpeak.Core.AI;

namespace SafeSpeak.Core.Tests;

public sealed class LocalEndpointIntentClassifierTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://192.168.1.50:11434")]
    [InlineData("file:///tmp/model")]
    public void Constructor_RejectsNonLoopbackEndpoints(string endpoint)
    {
        Assert.Throws<ArgumentException>(() =>
            new LocalEndpointIntentClassifier(
                endpoint,
                fallback: new HeuristicIntentClassifier()));
    }

    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://[::1]:11434")]
    public void Constructor_AcceptsLoopbackEndpoints(string endpoint)
    {
        using var classifier = new LocalEndpointIntentClassifier(
            endpoint,
            fallback: new HeuristicIntentClassifier());

        Assert.True(classifier.IsModelLoaded);
    }
}
