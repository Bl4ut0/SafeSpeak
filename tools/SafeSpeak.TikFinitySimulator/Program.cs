using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:21213");

WebApplication app = builder.Build();
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new
{
    service = "SafeSpeak TikFinity Simulator",
    websocket = "ws://localhost:21213/",
}));

app.Map("/", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Connect with WebSocket to ws://localhost:21213/");
        return;
    }

    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
    foreach (object demoEvent in DemoEvents.All)
    {
        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(demoEvent));
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, context.RequestAborted);
        await Task.Delay(TimeSpan.FromMilliseconds(650), context.RequestAborted);
    }

    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Demo complete", context.RequestAborted);
});

await app.RunAsync();

internal static class DemoEvents
{
    public static IReadOnlyList<object> All { get; } =
    [
        Chat("demo-1", "friendly_viewer", "Hello from the SafeSpeak simulator", isFollower: true),
        Chat("demo-2", "spacing_test", "b a d w o r d"),
        Chat("demo-3", "invisible_test", "bad\u200Bword"),
        Chat("demo-4", "mixed_script", "p\u0430ypal"),
        Chat("demo-5", "thai_script", "\u0e17\u0e14\u0e2a\u0e2d\u0e1a"),
        new { @event = "gift", data = new { uniqueId = "gifter", giftId = 5655 } },
        new { @event = "chat", data = new { uniqueId = "malformed_without_comment" } },
    ];

    private static object Chat(string id, string user, string comment, bool isFollower = false) => new
    {
        @event = "chat",
        data = new
        {
            msgId = id,
            uniqueId = user,
            nickname = user,
            comment,
            isFollower,
            createTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        },
    };
}
