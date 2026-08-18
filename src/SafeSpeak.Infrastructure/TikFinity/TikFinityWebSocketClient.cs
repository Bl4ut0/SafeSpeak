using System.Net.WebSockets;
using SafeSpeak.Core.Chat;

namespace SafeSpeak.Infrastructure.TikFinity;

public sealed class TikFinityWebSocketClient(
    Uri? endpoint = null,
    int maximumMessageBytes = 64 * 1024) : IChatEventSource
{
    private readonly Uri _endpoint = endpoint ?? new("ws://localhost:21213/");
    private readonly CancellationTokenSource _disposeCancellation = new();
    private ClientWebSocket? _socket;
    private ConnectionState _state = ConnectionState.Disconnected;

    public event EventHandler<ChatMessage>? MessageReceived;

    public event EventHandler<ConnectionState>? ConnectionStateChanged;

    public ConnectionState State => _state;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);

        TimeSpan delay = TimeSpan.FromSeconds(1);
        bool firstAttempt = true;

        while (!linkedCancellation.IsCancellationRequested)
        {
            SetState(firstAttempt ? ConnectionState.Connecting : ConnectionState.Reconnecting);
            firstAttempt = false;

            try
            {
                _socket?.Dispose();
                _socket = new ClientWebSocket();
                _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await _socket.ConnectAsync(_endpoint, linkedCancellation.Token);
                SetState(ConnectionState.Connected);
                delay = TimeSpan.FromSeconds(1);
                await ReceiveLoopAsync(_socket, linkedCancellation.Token);
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is WebSocketException or IOException or InvalidOperationException)
            {
                SetState(ConnectionState.Faulted);
            }

            if (!linkedCancellation.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(delay, linkedCancellation.Token);
                }
                catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
                {
                    break;
                }

                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }

        SetState(ConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeCancellation.CancelAsync();
        if (_socket is { State: WebSocketState.Open })
        {
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "SafeSpeak stopping",
                    closeTimeout.Token);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
            {
                // The remote endpoint may already be gone.
            }
        }

        _socket?.Dispose();
        _disposeCancellation.Dispose();
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] receiveBuffer = new byte[8192];
        using var messageBuffer = new MemoryStream();

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(receiveBuffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                if (result.EndOfMessage)
                {
                    messageBuffer.SetLength(0);
                }

                continue;
            }

            if (messageBuffer.Length + result.Count > maximumMessageBytes)
            {
                messageBuffer.SetLength(0);
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Event exceeds SafeSpeak limit", cancellationToken);
                return;
            }

            messageBuffer.Write(receiveBuffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            if (TikFinityEventParser.TryParseChatMessage(
                    messageBuffer.GetBuffer().AsMemory(0, checked((int)messageBuffer.Length)),
                    out ChatMessage? message) && message is not null)
            {
                MessageReceived?.Invoke(this, message);
            }

            messageBuffer.SetLength(0);
        }
    }

    private void SetState(ConnectionState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        ConnectionStateChanged?.Invoke(this, state);
    }
}
