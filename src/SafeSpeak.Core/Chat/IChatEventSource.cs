namespace SafeSpeak.Core.Chat;

public interface IChatEventSource : IAsyncDisposable
{
    event EventHandler<ChatMessage>? MessageReceived;

    event EventHandler<ConnectionState>? ConnectionStateChanged;

    ConnectionState State { get; }

    Task RunAsync(CancellationToken cancellationToken);
}
