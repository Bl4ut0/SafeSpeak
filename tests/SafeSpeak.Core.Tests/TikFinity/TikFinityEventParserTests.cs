using System.Text;
using SafeSpeak.Core.Chat;
using SafeSpeak.Infrastructure.TikFinity;

namespace SafeSpeak.Core.Tests.TikFinity;

public sealed class TikFinityEventParserTests
{
    [Fact]
    public void ParsesFlatTikFinityChatPayload()
    {
        const string json = """
            {
              "event": "chat",
              "data": {
                "msgId": "42",
                "uniqueId": "viewer_name",
                "nickname": "Viewer Name",
                "comment": "Hello stream",
                "isFollower": true,
                "isSubscriber": true,
                "createTime": 1700000000
              }
            }
            """;

        bool parsed = TikFinityEventParser.TryParseChatMessage(Encoding.UTF8.GetBytes(json), out ChatMessage? message);

        Assert.True(parsed);
        Assert.NotNull(message);
        Assert.Equal("42", message.MessageId);
        Assert.Equal("viewer_name", message.UserId);
        Assert.Equal("Hello stream", message.Text);
        Assert.True(message.AudienceRole.HasFlag(AudienceRole.Follower));
        Assert.True(message.AudienceRole.HasFlag(AudienceRole.Subscriber));
    }

    [Fact]
    public void ParsesNestedUserShape()
    {
        const string json = """
            {
              "event": "chat",
              "data": {
                "message": "Nested payload",
                "user": {
                  "uniqueId": "nested_user",
                  "nickname": "Nested User",
                  "isModerator": true
                }
              }
            }
            """;

        bool parsed = TikFinityEventParser.TryParseChatMessage(Encoding.UTF8.GetBytes(json), out ChatMessage? message);

        Assert.True(parsed);
        Assert.Equal("nested_user", message?.UserId);
        Assert.True(message?.AudienceRole.HasFlag(AudienceRole.Moderator));
    }

    [Theory]
    [InlineData("{not valid json")]
    [InlineData("{\"event\":\"gift\",\"data\":{}}")]
    [InlineData("{\"event\":\"chat\",\"data\":{}}")]
    public void IgnoresMalformedAndNonChatPayloads(string json)
    {
        Assert.False(TikFinityEventParser.TryParseChatMessage(Encoding.UTF8.GetBytes(json), out _));
    }
}
