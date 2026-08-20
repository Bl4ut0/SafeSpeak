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

public interface ITikFinityConnector : IAsyncDisposable
{
    ConnectionState State { get; }
    string EndpointUrl { get; }
    event EventHandler<ChatMessage>? MessageReceived;
    event EventHandler<LivestreamEvent>? EventReceived;
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}
