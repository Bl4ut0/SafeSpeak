using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class SourceConnectorHostTests
{
    [Fact]
    public async Task ReplaceDisposesOldSourceAndRelaysOnlyTheSelectedSource()
    {
        var first = new FakeConnector("first");
        var second = new FakeConnector("second");
        await using var host = new SourceConnectorHost(first);
        var received = new List<string>();
        host.EventReceived += (_, liveEvent) => received.Add(liveEvent.Text);

        first.Emit("before switch");
        await host.ReplaceAsync(() => second);
        first.Emit("from old source");
        second.Emit("from selected source");

        Assert.True(first.WasDisposed);
        Assert.Equal("second", host.Descriptor.Id);
        Assert.Equal(["before switch", "from selected source"], received);
    }

    private sealed class FakeConnector(string id) : ISourceConnector
    {
        public SourceConnectorDescriptor Descriptor { get; } = new(
            id,
            id,
            id,
            "Test source",
            SourceConnectorCapabilities.Chat);
        public ConnectionState State => ConnectionState.Disconnected;
        public string EndpointDescription => "Test";
        public bool WasDisposed { get; private set; }
        public event EventHandler<LivestreamEvent>? EventReceived;
        public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

        public void Emit(string text) => EventReceived?.Invoke(this, new LivestreamEvent
        {
            Type = LivestreamEventType.Chat,
            Text = text
        });

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(ConnectionState.Connected));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
