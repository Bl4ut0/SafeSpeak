using System.Net;
using System.Net.Sockets;
using System.Text;
using SafeSpeak.Core.Ipc;

namespace SafeSpeak.Core.Tests;

public sealed class StreamDeckIpcServerTests
{
    [Fact]
    public async Task Command_RequiresPostAndClientMarker()
    {
        int port = ReservePort();
        string? received = null;
        using var server = new StreamDeckIpcServer(
            () => new IpcStateBroadcast(),
            (command, _) =>
            {
                received = command;
                return Task.FromResult("ok");
            },
            port);
        server.Start();
        using var client = new HttpClient();

        HttpResponseMessage get = await client.GetAsync($"http://127.0.0.1:{port}/command?cmd=arm");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/command");
        request.Headers.Add("X-SafeSpeak-Client", "streamdeck");
        request.Content = new StringContent("{\"Command\":\"arm\",\"Parameter\":\"\"}", Encoding.UTF8, "application/json");
        HttpResponseMessage post = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Equal("arm", received);
    }

    [Fact]
    public async Task WebPageOrigin_CannotReadOrControlLoopbackServer()
    {
        int port = ReservePort();
        using var server = new StreamDeckIpcServer(
            () => new IpcStateBroadcast(),
            (_, _) => Task.FromResult("ok"),
            port);
        server.Start();
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/state");
        request.Headers.Add("Origin", "https://example.invalid");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
