using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public class TikFinityParserTests
{
    [Fact]
    public void ParseTikFinityEvent_ParsesStandardChatPayloadCorrectly()
    {
        string json = """
        {
            "event": "chat",
            "data": {
                "comment": "Hello world from TikFinity!",
                "uniqueId": "stream_fan_42",
                "nickname": "Stream Fan",
                "isSubscriber": true,
                "isModerator": false,
                "followRole": 1
            }
        }
        """;

        var msg = TikFinityWebSocketClient.ParseTikFinityEvent(json);

        Assert.NotNull(msg);
        Assert.Equal("Hello world from TikFinity!", msg.RawText);
        Assert.Equal("stream_fan_42", msg.Author);
        Assert.Equal("Stream Fan", msg.AuthorDisplayName);
        Assert.Equal(AuthorTier.Subscriber, msg.AuthorTier);
        Assert.True(msg.IsSubscriber);
    }

    [Fact]
    public void ParseTikFinityEvent_IgnoresNonChatEvents()
    {
        string json = """
        {
            "event": "gift",
            "data": {
                "giftName": "Rose",
                "giftCount": 10
            }
        }
        """;

        var msg = TikFinityWebSocketClient.ParseTikFinityEvent(json);
        Assert.Null(msg);
    }

    [Fact]
    public void ParseTikFinityEvent_HandlesMalformedJsonGracefullyWithoutThrowing()
    {
        string invalidJson = "{ invalid json content ...";
        var msg = TikFinityWebSocketClient.ParseTikFinityEvent(invalidJson);

        Assert.Null(msg);
    }
}
