using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using SafeSpeak.Core.Logging;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Connectors.TikTok;

internal interface ITikTokLiveSession
{
    Task<bool> RunAsync(string username, TikTokEventDecoder decoder, Action connected,
        Action<LivestreamEvent> received, CancellationToken cancellationToken);
}

internal sealed class TikTokConnectionException(string message, bool retryable = false) : Exception(message)
{
    public bool Retryable { get; } = retryable;
}

internal sealed class TikTokLiveSession : ITikTokLiveSession
{
    internal const int MaximumFrameBytes = 256 * 1024;
    internal const int MaximumDecodedBytes = 1024 * 1024;
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
    private readonly Func<HttpClient> _createHttp;
    private readonly Func<Uri, string, CancellationToken, Task<WebSocket>> _openSocket;

    public TikTokLiveSession() : this(
        () => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(12) }, OpenSocketAsync) { }

    internal TikTokLiveSession(Func<HttpClient> createHttp,
        Func<Uri, string, CancellationToken, Task<WebSocket>> openSocket)
    {
        _createHttp = createHttp;
        _openSocket = openSocket;
    }

    public async Task<bool> RunAsync(string username, TikTokEventDecoder decoder, Action connected,
        Action<LivestreamEvent> received, CancellationToken cancellationToken)
    {
        using var http = _createHttp();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua", "\"Chromium\";v=\"130\", \"Google Chrome\";v=\"130\", \"Not?A_Brand\";v=\"99\"");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        using var setup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        setup.CancelAfter(TimeSpan.FromSeconds(20));
        AppLogger.LogInformation("TikTokLiveSession", $"Resolving room ID for @{username}...");
        string roomId = await ResolveRoomAsync(http, username, setup.Token).ConfigureAwait(false);
        AppLogger.LogInformation("TikTokLiveSession", $"Resolved room ID {roomId} for @{username}. Obtaining session cookie...");
        string cookie = await GetCookieAsync(http, username, setup.Token).ConfigureAwait(false);
        WebSocket socket;
        try
        {
            socket = await _openSocket(BuildWebSocketUri(roomId), cookie, setup.Token).ConfigureAwait(false);
        }
        catch
        {
            ClearCachedTtwid();
            throw;
        }
        using var _ = socket;
        AppLogger.LogInformation("TikTokLiveSession", $"WebSocket connected to room {roomId}. Sending handshake...");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var sendGate = new SemaphoreSlim(1, 1);
        async Task Send(byte[] bytes, CancellationToken ct)
        {
            await sendGate.WaitAsync(ct).ConfigureAwait(false);
            try { await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false); }
            finally { sendGate.Release(); }
        }
        ulong room = ulong.Parse(roomId, CultureInfo.InvariantCulture);
        await Send(Frame("hb", new TikTokProtobuf.Writer().Number(1, room).Build()), lifetime.Token).ConfigureAwait(false);
        await Send(Frame("im_enter_room", new TikTokProtobuf.Writer().Number(1, room).Number(4, 12)
            .Text(5, "audience").Text(9, "1").Build()), lifetime.Token).ConfigureAwait(false);
        Task heartbeat = HeartbeatAsync(room, Send, lifetime.Token);
        Task<bool> receive = ReceiveAsync(socket, roomId, decoder, connected, received, Send, lifetime.Token);
        try
        {
            Task finished = await Task.WhenAny(heartbeat, receive).ConfigureAwait(false);
            if (finished == receive) return await receive.ConfigureAwait(false);
            await heartbeat.ConfigureAwait(false);
            throw new IOException("TikTok heartbeat stopped.");
        }
        finally
        {
            lifetime.Cancel();
            socket.Abort();
            // Observe both tasks and stop both directions before disposing socket/gate.
            try { await Task.WhenAll(heartbeat, receive).ConfigureAwait(false); }
            catch (Exception) { /* The selected task's error is propagated above. */ }
        }
    }

    private static async Task<string> ResolveRoomAsync(HttpClient http, string username, CancellationToken ct)
    {
        string url = "https://www.tiktok.com/api-live/user/room?aid=1988&app_name=tiktok_web&device_platform=web_pc" +
            "&app_language=en&browser_language=en-US&user_is_login=false&sourceType=54&staleTime=0&uniqueId=" + Uri.EscapeDataString(username.ToLowerInvariant());
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        CheckStatus(response.StatusCode);
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        byte[] bytes = await ReadBoundedAsync(stream, MaximumFrameBytes, ct).ConfigureAwait(false);
        return ParseRoomResponse(bytes);
    }

    internal static string ParseRoomResponse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (!root.TryGetProperty("statusCode", out var status) || !status.TryGetInt64(out long code))
                throw new TikTokConnectionException("TikTok returned an unsupported room response. Try again later.");
            if (code == 19881007) throw new TikTokConnectionException("TikTok username was not found. Check the username and reconnect.");
            if (code != 0) throw new TikTokConnectionException("TikTok did not allow this room lookup. Try again later.");
            if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("user", out var user))
                throw new TikTokConnectionException("TikTok did not return a public LIVE room.");
            string roomId = user.TryGetProperty("roomId", out var id) ? id.ToString() : "";
            ulong liveStatus = 0;
            if (data.TryGetProperty("liveRoom", out var liveRoom) && liveRoom.TryGetProperty("status", out var live))
                ulong.TryParse(live.ToString(), out liveStatus);
            else if (user.TryGetProperty("status", out var userStatus)) ulong.TryParse(userStatus.ToString(), out liveStatus);
            if (!ulong.TryParse(roomId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong numericId) || numericId == 0 || liveStatus != 2)
                throw new TikTokConnectionException("This username is not LIVE. SafeSpeak will check again automatically.", retryable: true);
            return numericId.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new TikTokConnectionException("TikTok returned an unreadable room response. Direct access may be unavailable.");
        }
    }

    internal static string? CachedTtwid { get; set; }

    internal static void ClearCachedTtwid() => CachedTtwid = null;

    internal static string? ExtractTtwidCookie(string header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;

        int idx = 0;
        while (idx < header.Length)
        {
            int found = header.IndexOf("ttwid=", idx, StringComparison.OrdinalIgnoreCase);
            if (found < 0) break;

            if (found == 0 || header[found - 1] == ' ' || header[found - 1] == ';' || header[found - 1] == ',')
            {
                int end = header.IndexOfAny([';', ','], found);
                string candidate = (end < 0 ? header[found..] : header[found..end]).Trim();
                int eq = candidate.IndexOf('=');
                if (eq >= 0 && eq + 1 < candidate.Length)
                {
                    string value = candidate[(eq + 1)..].Trim();
                    if (value.Length > 0 && candidate.Length <= 4096 && !candidate.Any(char.IsControl))
                    {
                        return "ttwid=" + value;
                    }
                }
            }
            idx = found + 6;
        }
        return null;
    }

    internal static async Task<string> GetCookieAsync(HttpClient http, string username, CancellationToken ct, bool useCache = true)
    {
        if (useCache && !string.IsNullOrEmpty(CachedTtwid))
        {
            AppLogger.LogDebug("TikTokLiveSession", "Reusing cached session cookie.");
            return CachedTtwid;
        }

        // Endpoint candidates in order of retrieval reliability:
        // 1. Root origin: https://www.tiktok.com/
        // 2. Creator live page: https://www.tiktok.com/@username/live
        // 3. Live exploration portal: https://www.tiktok.com/live
        // 4. Explore portal: https://www.tiktok.com/explore
        var endpoints = new[]
        {
            new Uri("https://www.tiktok.com/"),
            new Uri("https://www.tiktok.com/@" + Uri.EscapeDataString(username) + "/live"),
            new Uri("https://www.tiktok.com/live"),
            new Uri("https://www.tiktok.com/explore")
        };

        for (int retry = 0; retry < 2; retry++)
        {
            foreach (var uri in endpoints)
            {
                if (ct.IsCancellationRequested) break;
                string? cookie = await TryFetchCookieAsync(http, uri, ct).ConfigureAwait(false);
                if (cookie is not null)
                {
                    AppLogger.LogDebug("TikTokLiveSession", $"Acquired session cookie from {uri.AbsolutePath}.");
                    if (useCache) CachedTtwid = cookie;
                    return cookie;
                }
            }

            if (retry == 0 && !ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }

        throw new TikTokConnectionException("TikTok did not establish a public viewing session. SafeSpeak will retry.", retryable: true);
    }

    private static async Task<string?> TryFetchCookieAsync(HttpClient http, Uri uri, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            AppLogger.LogDebug("TikTokLiveSession", $"Cookie fetch from {uri} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (string header in cookies)
                {
                    string? cookie = ExtractTtwidCookie(header);
                    if (cookie is not null) return cookie;
                }
            }

            if ((int)response.StatusCode == 429)
            {
                throw new TikTokConnectionException("TikTok is limiting connections. SafeSpeak will retry.", retryable: true);
            }
        }
        catch (TikTokConnectionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            AppLogger.LogDebug("TikTokLiveSession", $"Cookie fetch from {uri} failed: {ex.Message}");
        }
        return null;
    }

    private static void CheckStatus(HttpStatusCode status)
    {
        if ((int)status == 429) throw new TikTokConnectionException("TikTok is limiting connections. SafeSpeak will retry.", retryable: true);
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            throw new TikTokConnectionException("TikTok refused direct access. This test supports public LIVE streams only.");
        if ((int)status >= 500) throw new TikTokConnectionException("TikTok is temporarily unavailable. SafeSpeak will retry.", retryable: true);
        if ((int)status is < 200 or >= 300) throw new TikTokConnectionException("TikTok returned an unsupported response. Try again later.");
    }

    internal static Uri BuildWebSocketUri(string roomId)
    {
        if (!ulong.TryParse(roomId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) || id == 0)
            throw new ArgumentException("A numeric TikTok room ID is required.", nameof(roomId));
        return new Uri("wss://webcast-ws.tiktok.com/webcast/im/ws_proxy/ws_reuse_supplement/?" +
            "version_code=180800&device_platform=web&cookie_enabled=true&screen_width=1920&screen_height=1080" +
            "&browser_language=en-US&browser_platform=Win32&browser_name=Mozilla&browser_version=5.0&browser_online=true" +
            "&tz_name=" + Uri.EscapeDataString(TimeZoneInfo.Local.Id) +
            "&app_name=tiktok_web&sup_ws_ds_opt=1&update_version_code=2.0.0&compress=gzip&webcast_language=en" +
            "&ws_direct=1&aid=1988&live_id=12&app_language=en&client_enter=1&room_id=" + roomId +
            "&identity=audience&history_comment_count=0&last_rtt=150.000&heartbeat_duration=10000&resp_content_type=protobuf&did_rule=3");
    }

    private static async Task<WebSocket> OpenSocketAsync(Uri uri, string cookie, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("User-Agent", UserAgent);
        socket.Options.SetRequestHeader("Cookie", cookie);
        socket.Options.SetRequestHeader("Origin", "https://www.tiktok.com");
        try { await socket.ConnectAsync(uri, ct).ConfigureAwait(false); return socket; }
        catch { socket.Dispose(); throw; }
    }

    private static async Task HeartbeatAsync(ulong room, Func<byte[], CancellationToken, Task> send, CancellationToken ct)
    {
        byte[] heartbeat = Frame("hb", new TikTokProtobuf.Writer().Number(1, room).Build());
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            await send(heartbeat, ct).ConfigureAwait(false);
        }
    }

    private static async Task<bool> ReceiveAsync(WebSocket socket, string roomId, TikTokEventDecoder decoder,
        Action connected, Action<LivestreamEvent> received, Func<byte[], CancellationToken, Task> send, CancellationToken ct)
    {
        byte[] buffer = new byte[8192];
        bool confirmed = false;
        long rateWindow = Environment.TickCount64;
        int rateCount = 0;
        while (true)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return false;
                if (result.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException("Unexpected TikTok frame type.");
                if (message.Length + result.Count > MaximumFrameBytes) throw new InvalidDataException("TikTok frame exceeded the safety limit.");
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            var frame = new TikTokProtobuf(message.ToArray());
            string type = frame.Text(7, 128);
            if (type != "msg") continue;
            byte[] payload = Decompress(frame.Bytes(8));
            var response = new TikTokProtobuf(payload);
            if (response.Number(9) != 0)
                await send(Frame("ack", response.Bytes(5).ToArray(), frame.Number(2)), ct).ConfigureAwait(false);
            // A decoded event response confirms the stream, not merely the TCP handshake.
            if (!confirmed)
            {
                ct.ThrowIfCancellationRequested();
                confirmed = true;
                AppLogger.LogInformation("TikTokLiveSession", $"Stream confirmed for room {roomId}. Receiving live events...");
                connected();
            }
            int batchCount = 0;
            foreach (var entry in response.Repeated(1))
            {
                ct.ThrowIfCancellationRequested();
                if (++batchCount > 512) throw new InvalidDataException("TikTok event batch exceeded the safety limit.");
                var envelope = new TikTokProtobuf(entry);
                string method = envelope.Text(1, 128);
                var body = envelope.Bytes(2);
                bool history = envelope.Number(6) != 0;
                if (!history && method == "WebcastControlMessage" && new TikTokProtobuf(body).Number(2) == 3)
                {
                    AppLogger.LogInformation("TikTokLiveSession", $"Stream ended signal received for room {roomId}.");
                    return true;
                }
                if (Environment.TickCount64 - rateWindow >= 1000) { rateCount = 0; rateWindow = Environment.TickCount64; }
                if (++rateCount > 200) continue;
                var liveEvent = decoder.Decode(method, body, roomId, envelope.Number(3), history, DateTimeOffset.UtcNow);
                if (liveEvent is not null) received(liveEvent);
            }
        }
    }

    internal static byte[] Decompress(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length < 2 || bytes.Span[0] != 0x1f || bytes.Span[1] != 0x8b) return bytes.ToArray();
        using var input = new MemoryStream(bytes.ToArray());
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + count > MaximumDecodedBytes) throw new InvalidDataException("TikTok decompressed data exceeded the safety limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken ct)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("TikTok HTTP response exceeded the safety limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    internal static byte[] Frame(string type, byte[] payload, ulong logId = 0) =>
        new TikTokProtobuf.Writer().Number(2, logId).Text(6, "pb").Text(7, type).Bytes(8, payload).Build();
}
