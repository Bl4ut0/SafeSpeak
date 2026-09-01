using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Connectors;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Faulted
}

public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionState State { get; }
    public string Message { get; }

    public ConnectionStateChangedEventArgs(ConnectionState state, string message = "")
    {
        State = state;
        Message = message;
    }
}

[Flags]
public enum SourceConnectorCapabilities
{
    None = 0,
    Chat = 1 << 0,
    Gifts = 1 << 1,
    Follows = 1 << 2,
    Shares = 1 << 3,
    Subscriptions = 1 << 4,
    Joins = 1 << 5,
    Likes = 1 << 6
}

public sealed record SourceConnectorDescriptor(
    string Id,
    string DisplayName,
    string ProviderName,
    string ConnectionDescription,
    SourceConnectorCapabilities Capabilities,
    bool SupportsAutomaticReconnect = true);

/// <summary>
/// Platform-neutral source contract. Connectors normalize provider payloads to
/// LivestreamEvent before the application sees them.
/// </summary>
public interface ISourceConnector : IAsyncDisposable
{
    SourceConnectorDescriptor Descriptor { get; }
    ConnectionState State { get; }
    string EndpointDescription { get; }
    event EventHandler<LivestreamEvent>? EventReceived;
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}
