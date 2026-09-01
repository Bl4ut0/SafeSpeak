using SafeSpeak.Core.AI;
using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Mobile.Foundation.Tests;

public sealed class MobileFoundationTests
{
    [Fact]
    public void PortableDefaultUsesDependencyFreeClassifier()
    {
        using IIntentClassifier classifier = IntentClassifierDefaults.CreateLocal();

        Assert.IsType<HeuristicIntentClassifier>(classifier);
    }

    [Fact]
    public void NormalizedEventPreservesSourcePlatform()
    {
        var livestreamEvent = new LivestreamEvent
        {
            Platform = "YouTube Live",
            Type = LivestreamEventType.Chat,
            Author = "viewer",
            Text = "Hello"
        };

        Assert.Equal("YouTube Live", livestreamEvent.ToChatMessage().Platform);
    }

    [Fact]
    public async Task PortablePipelineApprovesSafeMessage()
    {
        using var pipeline = new ModerationPipeline(
            intentClassifier: new HeuristicIntentClassifier());

        ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Platform = "Offline simulator",
            Author = "tester",
            RawText = "Thanks for the great stream today!"
        });

        Assert.True(decision.Passed);
        Assert.NotEmpty(decision.SpokenText);
    }

    [Fact]
    public async Task PortablePipelineRejectsThreat()
    {
        using var pipeline = new ModerationPipeline(
            intentClassifier: new HeuristicIntentClassifier());

        ModerationDecision decision = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Platform = "Offline simulator",
            Author = "tester",
            RawText = "I know where you live and I am coming for you"
        });

        Assert.False(decision.Passed);
    }

    [Fact]
    public void UnimplementedConnectorsAreClearlyMarked()
    {
        ConnectorRoadmapItem offline = Assert.Single(
            ConnectorRoadmap.All.Where(item => item.Availability == ConnectorAvailability.Available));

        Assert.Equal("offline-simulator", offline.Id);
        Assert.All(
            ConnectorRoadmap.All.Where(item => item.Id != offline.Id),
            item => Assert.NotEqual(ConnectorAvailability.Available, item.Availability));
    }
}
