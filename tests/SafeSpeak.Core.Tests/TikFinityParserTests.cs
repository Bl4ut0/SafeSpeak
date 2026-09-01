using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public class TikFinityParserTests
{
    [Fact]
    public void ParseLivestreamEvent_ParsesStandardChatPayloadCorrectly()
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

        var liveEvent = TikFinityWebSocketClient.ParseLivestreamEvent(json);

        Assert.NotNull(liveEvent);
        Assert.Equal(LivestreamEventType.Chat, liveEvent.Type);
        Assert.Equal("Hello world from TikFinity!", liveEvent.Text);
        Assert.Equal("stream_fan_42", liveEvent.Author);
        Assert.Equal("Stream Fan", liveEvent.AuthorDisplayName);
        Assert.Equal(AuthorTier.Subscriber, liveEvent.AuthorTier);
        Assert.True(liveEvent.IsSubscriber);
    }

    [Fact]
    public void ParseLivestreamEvent_IgnoresUnsupportedEvents()
    {
        string json = """
        {
            "event": "unsupported",
            "data": {
                "giftName": "Rose",
                "giftCount": 10
            }
        }
        """;

        var liveEvent = TikFinityWebSocketClient.ParseLivestreamEvent(json);
        Assert.Null(liveEvent);
    }

    [Fact]
    public void ParseLivestreamEvent_ParsesGiftDetails()
    {
        const string json = """
        { "event": "gift", "data": { "nickname": "Ada", "uniqueId": "ada_1", "giftName": "Rose", "giftCount": 3, "diamondCount": 3 } }
        """;

        var liveEvent = TikFinityWebSocketClient.ParseLivestreamEvent(json);

        Assert.NotNull(liveEvent);
        Assert.Equal(LivestreamEventType.Gift, liveEvent.Type);
        Assert.Equal("Ada", liveEvent.AuthorDisplayName);
        Assert.Equal("Rose", liveEvent.GiftName);
        Assert.Equal(3, liveEvent.GiftCount);
    }

    [Theory]
    [InlineData("follow", LivestreamEventType.Follow)]
    [InlineData("share", LivestreamEventType.Share)]
    [InlineData("subscribe", LivestreamEventType.Subscribe)]
    [InlineData("join", LivestreamEventType.Join)]
    [InlineData("like", LivestreamEventType.Like)]
    public void ParseLivestreamEvent_RecognizesSupportedEvents(string eventName, LivestreamEventType expected)
    {
        var liveEvent = TikFinityWebSocketClient.ParseLivestreamEvent($$"""{ "event": "{{eventName}}", "data": { "nickname": "Viewer" } }""");
        Assert.NotNull(liveEvent);
        Assert.Equal(expected, liveEvent.Type);
    }

    [Fact]
    public void ParseLivestreamEvent_HandlesMalformedJsonGracefullyWithoutThrowing()
    {
        string invalidJson = "{ invalid json content ...";
        var liveEvent = TikFinityWebSocketClient.ParseLivestreamEvent(invalidJson);

        Assert.Null(liveEvent);
    }
}
