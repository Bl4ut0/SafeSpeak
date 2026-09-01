using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class TikFinityWebSocketClientLifecycleTests
{
    [Fact]
    public async Task TransportFailure_ReconnectsAndReceivesFromReplacementConnection()
    {
        await using var server = new LoopbackWebSocketServer();
        await using var connector = new TikFinityWebSocketClient(server.EndpointUrl);
        var states = new ConcurrentQueue<ConnectionState>();
        var received = new TaskCompletionSource<LivestreamEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connector.StateChanged += (_, args) => states.Enqueue(args.State);
        connector.EventReceived += (_, liveEvent) => received.TrySetResult(liveEvent);

        await connector.ConnectAsync();
        await using (LoopbackWebSocketConnection first =
                     await server.AcceptWebSocketAsync().WaitAsync(TestTimeout))
        {
            await WaitUntilAsync(() => connector.State == ConnectionState.Connected);
            first.Abort();
        }

        await WaitUntilAsync(() => states.Contains(ConnectionState.Reconnecting));
        await using LoopbackWebSocketConnection replacement =
            await server.AcceptWebSocketAsync().WaitAsync(TestTimeout);
        await replacement.SendTextAsync(ChatJson("reconnected", "Reconnect User"));

        LivestreamEvent liveEvent = await received.Task.WaitAsync(TestTimeout);
        Assert.Equal("reconnected", liveEvent.Text);
        Assert.Equal("Reconnect User", liveEvent.AuthorDisplayName);
        Assert.True(states.Count(state => state == ConnectionState.Connected) >= 2);
    }

    [Fact]
    public async Task OversizedPayload_IsRejectedAndMovesToReconnectState()
    {
        await using var server = new LoopbackWebSocketServer();
        await using var connector = new TikFinityWebSocketClient(server.EndpointUrl);
        var reconnect = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connector.StateChanged += (_, args) =>
        {
            if (args.State == ConnectionState.Reconnecting)
            {
                reconnect.TrySetResult(args.Message);
            }
        };

        await connector.ConnectAsync();
        await using LoopbackWebSocketConnection connection =
            await server.AcceptWebSocketAsync().WaitAsync(TestTimeout);
        await WaitUntilAsync(() => connector.State == ConnectionState.Connected);

        await connection.SendTextAsync(new string('x', (256 * 1024) + 1));

        string failure = await reconnect.Task.WaitAsync(TestTimeout);
        Assert.Contains("exceeded", failure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("256 KB", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellingConnectLifetime_StopsReceiveLoopAndDisconnects()
    {
        await using var server = new LoopbackWebSocketServer();
        await using var connector = new TikFinityWebSocketClient(server.EndpointUrl);
        using var cancellation = new CancellationTokenSource();

        await connector.ConnectAsync(cancellation.Token);
        await using LoopbackWebSocketConnection connection =
            await server.AcceptWebSocketAsync().WaitAsync(TestTimeout);
        await WaitUntilAsync(() => connector.State == ConnectionState.Connected);

        cancellation.Cancel();

        await WaitUntilAsync(() => connector.State == ConnectionState.Disconnected);
        Assert.Equal(ConnectionState.Disconnected, connector.State);
    }

    [Fact]
    public async Task RemoteClose_IsAcknowledgedAndRetriedWithVisibleState()
    {
        await using var server = new LoopbackWebSocketServer();
        await using var connector = new TikFinityWebSocketClient(server.EndpointUrl);
        var reconnect = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connector.StateChanged += (_, args) =>
        {
            if (args.State == ConnectionState.Reconnecting)
            {
                reconnect.TrySetResult(args.Message);
            }
        };

        await connector.ConnectAsync();
        await using LoopbackWebSocketConnection first =
            await server.AcceptWebSocketAsync().WaitAsync(TestTimeout);
        await WaitUntilAsync(() => connector.State == ConnectionState.Connected);

        await first.SendCloseAsync();

        string reason = await reconnect.Task.WaitAsync(TestTimeout);
        Assert.Contains("closed", reason, StringComparison.OrdinalIgnoreCase);
        await using LoopbackWebSocketConnection replacement =
            await server.AcceptWebSocketAsync().WaitAsync(TestTimeout);
        await WaitUntilAsync(() => connector.State == ConnectionState.Connected);
    }

    [Fact]
    public async Task DisposeDuringHandshake_CancelsPromptlyAndIsIdempotent()
    {
        await using var server = new LoopbackWebSocketServer();
        var connector = new TikFinityWebSocketClient(server.EndpointUrl);

        await connector.ConnectAsync();
        using TcpClient stalledHandshake =
            await server.AcceptTcpClientAsync().WaitAsync(TestTimeout);
        await WaitUntilAsync(() => connector.State == ConnectionState.Connecting);

        Task firstDispose = connector.DisposeAsync().AsTask();
        Task secondDispose = connector.DisposeAsync().AsTask();

        Assert.Same(firstDispose, secondDispose);
        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TestTimeout);
        Assert.Equal(ConnectionState.Disconnected, connector.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connector.ConnectAsync());
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private static string ChatJson(string text, string displayName) =>
        $$"""{ "event": "chat", "data": { "uniqueId": "viewer", "nickname": "{{displayName}}", "comment": "{{text}}" } }""";

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class LoopbackWebSocketServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public LoopbackWebSocketServer()
        {
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            EndpointUrl = $"ws://127.0.0.1:{port}/";
        }

        public string EndpointUrl { get; }

        public Task<TcpClient> AcceptTcpClientAsync(CancellationToken cancellationToken = default) =>
            _listener.AcceptTcpClientAsync(cancellationToken).AsTask();

        public async Task<LoopbackWebSocketConnection> AcceptWebSocketAsync(
            CancellationToken cancellationToken = default)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
            try
            {
                NetworkStream stream = client.GetStream();
                string request = await ReadHttpHeadersAsync(stream, cancellationToken);
                string key = request
                    .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                    .First(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                    .Split(':', 2)[1]
                    .Trim();
                string accept = Convert.ToBase64String(
                    SHA1.HashData(Encoding.ASCII.GetBytes(
                        key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                byte[] response = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    $"Sec-WebSocket-Accept: {accept}\r\n\r\n");
                await stream.WriteAsync(response, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                return new LoopbackWebSocketConnection(client, stream);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }

        private static async Task<string> ReadHttpHeadersAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            var singleByte = new byte[1];
            while (bytes.Count < 16 * 1024)
            {
                int read = await stream.ReadAsync(singleByte, cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("Client closed before completing the WebSocket handshake.");
                }

                bytes.Add(singleByte[0]);
                int count = bytes.Count;
                if (count >= 4 &&
                    bytes[count - 4] == '\r' && bytes[count - 3] == '\n' &&
                    bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
                {
                    return Encoding.ASCII.GetString(bytes.ToArray());
                }
            }

            throw new InvalidDataException("WebSocket handshake headers exceeded the test limit.");
        }
    }

    private sealed class LoopbackWebSocketConnection : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;

        public LoopbackWebSocketConnection(TcpClient client, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) =>
            SendFrameAsync(0x1, Encoding.UTF8.GetBytes(text), cancellationToken);

        public Task SendCloseAsync(CancellationToken cancellationToken = default)
        {
            byte[] status = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(status, (ushort)WebSocketCloseStatus.NormalClosure);
            return SendFrameAsync(0x8, status, cancellationToken);
        }

        public void Abort()
        {
            _client.Client.LingerState = new LingerOption(true, 0);
            _client.Close();
        }

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }

        private async Task SendFrameAsync(
            byte opcode,
            byte[] payload,
            CancellationToken cancellationToken)
        {
            var header = new byte[10];
            header[0] = (byte)(0x80 | opcode);
            int headerLength;
            if (payload.Length <= 125)
            {
                header[1] = (byte)payload.Length;
                headerLength = 2;
            }
            else if (payload.Length <= ushort.MaxValue)
            {
                header[1] = 126;
                BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), (ushort)payload.Length);
                headerLength = 4;
            }
            else
            {
                header[1] = 127;
                BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(2), (ulong)payload.Length);
                headerLength = 10;
            }

            await _stream.WriteAsync(header.AsMemory(0, headerLength), cancellationToken);
            await _stream.WriteAsync(payload, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
    }
}
