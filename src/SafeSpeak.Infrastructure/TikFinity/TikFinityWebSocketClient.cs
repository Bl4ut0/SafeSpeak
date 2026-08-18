using System.Net.WebSockets;
using SafeSpeak.Core.Chat;

namespace SafeSpeak.Infrastructure.TikFinity;

public sealed class TikFinityWebSocketClient(
    Uri? endpoint = null,
    int maximumMessageBytes = 64 * 1024) : IChatEventSource
{
    private readonly Uri _endpoint = endpoint ?? new("ws://localhost:21213/");
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Lock _statusLock = new();
    private ClientWebSocket? _socket;
    private ConnectionState _state = ConnectionState.Disconnected;
    private long _connectionAttempts;
    private long _textEventsReceived;
    private long _chatMessagesAccepted;
    private long _eventsIgnored;
    private DateTimeOffset? _lastChatMessageAt;
    private string? _lastError;

    public event EventHandler<ChatMessage>? MessageReceived;

    public event EventHandler<ConnectionState>? ConnectionStateChanged;

    public event EventHandler<TikFinityBridgeStatus>? StatusChanged;

    public ConnectionState State => _state;

    public TikFinityBridgeStatus Status
    {
        get
        {
            lock (_statusLock)
            {
                return CreateStatus();
            }
        }
    }

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

            lock (_statusLock)
            {
                _connectionAttempts++;
            }

            PublishStatus();

            try
            {
                _socket?.Dispose();
                _socket = new ClientWebSocket();
                _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await _socket.ConnectAsync(_endpoint, linkedCancellation.Token);
                SetState(ConnectionState.Connected);
                delay = TimeSpan.FromSeconds(1);
                await ReceiveLoopAsync(_socket, linkedCancellation.Token);
                if (!linkedCancellation.IsCancellationRequested)
                {
                    SetState(ConnectionState.Reconnecting);
                }
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is WebSocketException or IOException or InvalidOperationException)
            {
                SetState(ConnectionState.Faulted, exception.GetType().Name);
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
                if (socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseOutputAsync(
                        socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        "SafeSpeak acknowledged TikFinity close",
                        cancellationToken);
                }

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

            bool accepted = TikFinityEventParser.TryParseChatMessage(
                    messageBuffer.GetBuffer().AsMemory(0, checked((int)messageBuffer.Length)),
                    out ChatMessage? message) && message is not null;
            RecordTextEvent(accepted);
            if (accepted)
            {
                MessageReceived?.Invoke(this, message!);
            }

            messageBuffer.SetLength(0);
        }
    }

    private void RecordTextEvent(bool accepted)
    {
        lock (_statusLock)
        {
            _textEventsReceived++;
            if (accepted)
            {
                _chatMessagesAccepted++;
                _lastChatMessageAt = DateTimeOffset.Now;
            }
            else
            {
                _eventsIgnored++;
            }
        }

        PublishStatus();
    }

    private void SetState(ConnectionState state, string? error = null)
    {
        bool stateChanged;
        lock (_statusLock)
        {
            stateChanged = _state != state;
            _state = state;
            if (state == ConnectionState.Connected)
            {
                _lastError = null;
            }
            else if (error is not null)
            {
                _lastError = error;
            }
        }

        if (stateChanged)
        {
            ConnectionStateChanged?.Invoke(this, state);
        }

        PublishStatus();
    }

    private TikFinityBridgeStatus CreateStatus() => new(
        _state,
        _endpoint,
        _connectionAttempts,
        _textEventsReceived,
        _chatMessagesAccepted,
        _eventsIgnored,
        _lastChatMessageAt,
        _lastError);

    private void PublishStatus()
    {
        TikFinityBridgeStatus status;
        lock (_statusLock)
        {
            status = CreateStatus();
        }

        StatusChanged?.Invoke(this, status);
    }
}
