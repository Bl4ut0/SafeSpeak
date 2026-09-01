using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Connectors;

/// <summary>
/// Robust WebSocket client for TikFinity's local event feed (ws://localhost:21213/).
/// </summary>
public sealed class TikFinityWebSocketClient : ISourceConnector
{
    private const int MaximumMessageBytes = 256 * 1024;
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CloseAcknowledgementTimeout = TimeSpan.FromSeconds(1);
    private readonly Uri _serverUri;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _connectionLoopTask;
    private ConnectionState _state = ConnectionState.Disconnected;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _disposeLock = new();
    private Task? _disposeTask;
    private int _disposeStarted;

    public static SourceConnectorDescriptor ConnectorDescriptor { get; } = new(
        Id: "tikfinity",
        DisplayName: "TikFinity",
        ProviderName: "TikTok LIVE through TikFinity",
        ConnectionDescription: "Local TikFinity WebSocket on this computer",
        Capabilities:
            SourceConnectorCapabilities.Chat |
            SourceConnectorCapabilities.Gifts |
            SourceConnectorCapabilities.Follows |
            SourceConnectorCapabilities.Shares |
            SourceConnectorCapabilities.Subscriptions |
            SourceConnectorCapabilities.Joins |
            SourceConnectorCapabilities.Likes);

    public SourceConnectorDescriptor Descriptor => ConnectorDescriptor;

    public ConnectionState State
    {
        get { lock (_stateLock) return _state; }
        private set => SetState(value);
    }

    public string EndpointDescription => $"{ConnectorDescriptor.ConnectionDescription} ({_serverUri})";

    public event EventHandler<LivestreamEvent>? EventReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public TikFinityWebSocketClient(string endpointUrl = "ws://localhost:21213/")
    {
        _serverUri = new Uri(endpointUrl);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
            bool connectionLoopIsActive = _connectionLoopTask is { IsCompleted: false };
            if (connectionLoopIsActive)
            {
                return;
            }

            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Connecting and automatic reconnect run in the background, but the task is
            // retained so disconnect and disposal can cancel and observe its completion.
            _connectionLoopTask = StartConnectionLoopAsync(_cts.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            CancellationTokenSource? connectionCts = _cts;
            Task? connectionLoopTask = _connectionLoopTask;
            connectionCts?.Cancel();
            try { _webSocket?.Abort(); } catch { }

            if (connectionLoopTask is not null)
            {
                try
                {
                    await connectionLoopTask.WaitAsync(DisconnectTimeout);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is the normal disconnect path.
                }
                catch (TimeoutException)
                {
                    // A broken WebSocket implementation must not hold shutdown open.
                }
            }

            _webSocket?.Dispose();
            _webSocket = null;
            _connectionLoopTask = null;
            _cts = null;
            connectionCts?.Dispose();
            State = ConnectionState.Disconnected;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StartConnectionLoopAsync(CancellationToken cancellationToken)
    {
        int backoffDelayMs = 1000;
        const int maxDelayMs = 15000;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                State = ConnectionState.Connecting;
                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();

                using var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectTimeoutCts.Token);

                await _webSocket.ConnectAsync(_serverUri, linkedCts.Token);

                State = ConnectionState.Connected;
                backoffDelayMs = 1000; // Reset backoff on successful connection

                await ReceiveLoopAsync(_webSocket, cancellationToken);

                // A clean remote close is still a lost source connection. Surface the
                // retry state and use the same bounded backoff as transport failures so
                // an unavailable connector cannot cause a tight reconnect loop.
                if (!cancellationToken.IsCancellationRequested)
                {
                    SetState(ConnectionState.Reconnecting, "The local connector closed the connection.");
                    await Task.Delay(backoffDelayMs, cancellationToken);
                    backoffDelayMs = Math.Min(backoffDelayMs * 2, maxDelayMs);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetState(ConnectionState.Reconnecting, ex.Message);

                try
                {
                    await Task.Delay(backoffDelayMs, cancellationToken);
                    backoffDelayMs = Math.Min(backoffDelayMs * 2, maxDelayMs);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        State = ConnectionState.Disconnected;
    }

    private void SetState(ConnectionState value, string message = "")
    {
        bool shouldRaise;
        lock (_stateLock)
        {
            shouldRaise = _state != value || !string.IsNullOrWhiteSpace(message);
            _state = value;
        }

        // Event handlers may read State, so invoke them after releasing the lock.
        if (shouldRaise)
        {
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(value, message));
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    closeCts.CancelAfter(CloseAcknowledgementTimeout);
                    try
                    {
                        await socket.CloseOutputAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Acknowledge Close",
                            closeCts.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (WebSocketException) { }
                    return;
                }

                ms.Write(buffer, 0, result.Count);
                if (ms.Length > MaximumMessageBytes)
                {
                    throw new InvalidDataException(
                        $"Connector payload exceeded the {MaximumMessageBytes / 1024} KB safety limit.");
                }
            }
            while (!result.EndOfMessage);

            ms.Seek(0, SeekOrigin.Begin);
            string json = Encoding.UTF8.GetString(ms.ToArray());

            var liveEvent = ParseLivestreamEvent(json);
            if (liveEvent != null)
            {
                EventReceived?.Invoke(this, liveEvent);
            }
        }
    }

    /// <summary>Parses chat, gift, follow, share, subscribe, join, and like events.</summary>
    public static LivestreamEvent? ParseLivestreamEvent(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            LivestreamEventType type = LivestreamEventType.Chat;
            if (root.TryGetProperty("event", out var eventProp))
            {
                string eventType = eventProp.GetString()?.Trim().ToLowerInvariant() ?? "";
                type = eventType switch
                {
                    "chat" or "comment" => LivestreamEventType.Chat,
                    "gift" => LivestreamEventType.Gift,
                    "follow" => LivestreamEventType.Follow,
                    "share" => LivestreamEventType.Share,
                    "subscribe" or "subscription" or "sub" => LivestreamEventType.Subscribe,
                    "join" or "member" => LivestreamEventType.Join,
                    "like" => LivestreamEventType.Like,
                    _ => (LivestreamEventType)(-1)
                };
                if ((int)type < 0) return null;
            }

            JsonElement dataElem = root.TryGetProperty("data", out var data) ? data : root;

            string comment = "";
            if (dataElem.TryGetProperty("comment", out var c)) comment = c.GetString() ?? "";
            else if (dataElem.TryGetProperty("text", out var t)) comment = t.GetString() ?? "";
            else if (dataElem.TryGetProperty("message", out var m)) comment = m.GetString() ?? "";

            if (type == LivestreamEventType.Chat && string.IsNullOrWhiteSpace(comment)) return null;

            string author = "";
            if (dataElem.TryGetProperty("uniqueId", out var u)) author = u.GetString() ?? "";
            else if (dataElem.TryGetProperty("username", out var un)) author = un.GetString() ?? "";
            else if (dataElem.TryGetProperty("author", out var a)) author = a.GetString() ?? "";

            string displayName = author;
            if (dataElem.TryGetProperty("nickname", out var nick)) displayName = nick.GetString() ?? author;
            else if (dataElem.TryGetProperty("displayName", out var dn)) displayName = dn.GetString() ?? author;

            bool isSub = false;
            if (dataElem.TryGetProperty("isSubscriber", out var sub)) isSub = sub.GetBoolean();

            bool isMod = false;
            if (dataElem.TryGetProperty("isModerator", out var mod)) isMod = mod.GetBoolean();

            AuthorTier tier = AuthorTier.Viewer;
            if (isMod) tier = AuthorTier.Moderator;
            else if (isSub) tier = AuthorTier.Subscriber;
            else if (dataElem.TryGetProperty("followRole", out var fr) && fr.GetInt32() > 0) tier = AuthorTier.Follower;

            string giftName = dataElem.TryGetProperty("giftName", out var gn) ? gn.GetString() ?? "gift" : "gift";
            int giftCount = TryGetInt(dataElem, "giftCount", 1);
            int diamondCount = TryGetInt(dataElem, "diamondCount", 0);

            return new LivestreamEvent
            {
                Platform = "TikTok LIVE",
                Type = type,
                Author = author,
                AuthorDisplayName = displayName,
                Text = comment,
                GiftName = giftName,
                GiftCount = Math.Max(1, giftCount),
                DiamondCount = Math.Max(0, diamondCount),
                AuthorTier = tier,
                IsSubscriber = isSub,
                IsModerator = isMod,
                TimestampUtc = DateTimeOffset.UtcNow
            };
        }
        catch
        {
            // Defensive parsing: ignore malformed payloads safely
            return null;
        }
    }

    private static int TryGetInt(JsonElement element, string propertyName, int fallback)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : fallback;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_disposeTask is null)
            {
                Volatile.Write(ref _disposeStarted, 1);
                _disposeTask = DisposeCoreAsync();
            }

            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try { await DisconnectAsync(); }
        finally { _lifecycleGate.Dispose(); }
    }
}
