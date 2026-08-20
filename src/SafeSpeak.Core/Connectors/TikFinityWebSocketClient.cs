using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Connectors;

/// <summary>
/// Robust WebSocket client for TikFinity's local event feed (ws://localhost:21213/).
/// </summary>
public sealed class TikFinityWebSocketClient : ITikFinityConnector
{
    private readonly Uri _serverUri;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private ConnectionState _state = ConnectionState.Disconnected;
    private readonly object _stateLock = new();

    public ConnectionState State
    {
        get { lock (_stateLock) return _state; }
        private set
        {
            lock (_stateLock)
            {
                if (_state != value)
                {
                    _state = value;
                    StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(value));
                }
            }
        }
    }

    public string EndpointUrl => _serverUri.ToString();

    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<LivestreamEvent>? EventReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public TikFinityWebSocketClient(string endpointUrl = "ws://localhost:21213/")
    {
        _serverUri = new Uri(endpointUrl);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (State == ConnectionState.Connected || State == ConnectionState.Connecting)
        {
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        _ = StartConnectionLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnected", CancellationToken.None);
                }
            }
            catch
            {
                // Ignore disconnect exceptions during teardown
            }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
        }

        State = ConnectionState.Disconnected;
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
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                State = ConnectionState.Reconnecting;
                StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(ConnectionState.Reconnecting, ex.Message));

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
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Acknowledge Close", CancellationToken.None);
                    return;
                }

                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            ms.Seek(0, SeekOrigin.Begin);
            string json = Encoding.UTF8.GetString(ms.ToArray());

            var liveEvent = ParseLivestreamEvent(json);
            if (liveEvent != null)
            {
                EventReceived?.Invoke(this, liveEvent);
                if (liveEvent.Type == LivestreamEventType.Chat)
                {
                    MessageReceived?.Invoke(this, liveEvent.ToChatMessage());
                }
            }
        }
    }

    /// <summary>
    /// Defensively parses incoming TikFinity JSON payloads.
    /// Supports standard TikFinity event schemas and flat chat messages.
    /// </summary>
    public static ChatMessage? ParseTikFinityEvent(string json)
        => ParseLivestreamEvent(json) is { Type: LivestreamEventType.Chat } evt ? evt.ToChatMessage() : null;

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

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cts?.Dispose();
    }
}
