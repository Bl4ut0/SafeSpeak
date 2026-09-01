using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class SourceConnectorRegistryTests
{
    [Fact]
    public async Task DefaultRegistryExposesTikFinityThroughGenericContract()
    {
        SourceConnectorRegistry registry = SourceConnectorRegistry.CreateDefault();

        SourceConnectorDescriptor descriptor = Assert.Single(registry.Descriptors);
        Assert.Equal("tikfinity", descriptor.Id);
        Assert.True(descriptor.Capabilities.HasFlag(SourceConnectorCapabilities.Chat));

        ISourceConnector connector = registry.Create(descriptor.Id);
        Assert.Equal(descriptor, connector.Descriptor);
        await connector.DisposeAsync();
    }

    [Fact]
    public void DuplicateIdsAreRejectedCaseInsensitively()
    {
        var registry = new SourceConnectorRegistry();
        var descriptor = new SourceConnectorDescriptor(
            "source",
            "Source",
            "Provider",
            "Test",
            SourceConnectorCapabilities.Chat);
        registry.Register(descriptor, () => new FakeConnector(descriptor));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(descriptor with { Id = "SOURCE" }, () => new FakeConnector(descriptor)));
    }

    [Fact]
    public async Task OfflineSimulatorEmitsOneNormalizedEventPerInjectedMessage()
    {
        await using var simulator = new OfflineEventSimulator();
        LivestreamEvent? received = null;
        int eventCount = 0;
        simulator.EventReceived += (_, liveEvent) =>
        {
            received = liveEvent;
            eventCount++;
        };

        simulator.InjectMessage("Hello streamer", "Stream Fan", AuthorTier.Follower);

        Assert.Equal(1, eventCount);
        Assert.NotNull(received);
        Assert.Equal(LivestreamEventType.Chat, received.Type);
        Assert.Equal("Stream Fan", received.AuthorDisplayName);
        Assert.Equal("Hello streamer", received.Text);
        Assert.Equal(AuthorTier.Follower, received.AuthorTier);
    }

    private sealed class FakeConnector(SourceConnectorDescriptor descriptor) : ISourceConnector
    {
        public SourceConnectorDescriptor Descriptor { get; } = descriptor;
        public ConnectionState State => ConnectionState.Disconnected;
        public string EndpointDescription => "Test";
        public event EventHandler<LivestreamEvent>? EventReceived
        {
            add { }
            remove { }
        }
        public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
