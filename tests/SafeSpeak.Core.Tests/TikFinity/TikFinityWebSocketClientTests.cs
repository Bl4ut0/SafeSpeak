using SafeSpeak.Core.Chat;
using SafeSpeak.Infrastructure.TikFinity;

namespace SafeSpeak.Core.Tests.TikFinity;

public sealed class TikFinityWebSocketClientTests
{
    [Fact]
    public async Task ExposesPrivacySafeInitialBridgeStatus()
    {
        var endpoint = new Uri("ws://127.0.0.1:32123/");
        await using var client = new TikFinityWebSocketClient(endpoint);

        TikFinityBridgeStatus status = client.Status;

        Assert.Equal(ConnectionState.Disconnected, status.State);
        Assert.Equal(endpoint, status.Endpoint);
        Assert.Equal(0, status.ConnectionAttempts);
        Assert.Equal(0, status.TextEventsReceived);
        Assert.Equal(0, status.ChatMessagesAccepted);
        Assert.Equal(0, status.EventsIgnored);
        Assert.Null(status.LastChatMessageAt);
        Assert.Null(status.LastError);
    }
}
