using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Connectors.TikTok;
using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public sealed class TikTokLiveConnectorTests
{
    [Theory]
    [InlineData("@safe_speak", "safe_speak")]
    [InlineData("creator.name", "creator.name")]
    public void UsernameValidation_NormalizesSupportedNames(string input, string expected)
    {
        Assert.True(TikTokLiveConnector.TryNormalizeUsername(input, out string actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("bad/name")]
    [InlineData("trailing.")]
    public void UsernameValidation_RejectsUnsupportedNames(string input) =>
        Assert.False(TikTokLiveConnector.TryNormalizeUsername(input, out _));

    [Fact]
    public void Decoder_NormalizesChatAndSuppressesDuplicateAndHistory()
    {
        var decoder = new TikTokEventDecoder();
        byte[] chat = Chat("viewer_1", "Viewer One", "hello stream", 52);

        LivestreamEvent? first = decoder.Decode(
            "WebcastChatMessage", chat, "10", 52, false, DateTimeOffset.UtcNow);
        LivestreamEvent? duplicate = decoder.Decode(
            "WebcastChatMessage", chat, "10", 52, false, DateTimeOffset.UtcNow);
        LivestreamEvent? history = decoder.Decode(
            "WebcastChatMessage", Chat("viewer_2", "Viewer Two", "old", 53),
            "10", 53, true, DateTimeOffset.UtcNow);

        Assert.NotNull(first);
        Assert.Equal(LivestreamEventType.Chat, first.Type);
        Assert.Equal("viewer_1", first.Author);
        Assert.Equal("Viewer One", first.AuthorDisplayName);
        Assert.Equal("hello stream", first.Text);
        Assert.Null(duplicate);
        Assert.Null(history);
    }

    [Fact]
    public async Task DirectChatDisplayName_ReachesFinalSpokenAttribution()
    {
        var decoder = new TikTokEventDecoder();
        LivestreamEvent? liveEvent = decoder.Decode(
            "WebcastChatMessage",
            Chat("viewer_1", "Viewer One", "hello stream", 54),
            "10",
            54,
            false,
            DateTimeOffset.UtcNow);
        using var pipeline = new ModerationPipeline(
            new ModerationConfig { UserCooldownSeconds = 0 },
            intentClassifier: new AlwaysSafeIntentClassifier());

        ModerationDecision decision = await pipeline.ProcessMessageAsync(
            Assert.IsType<LivestreamEvent>(liveEvent).ToChatMessage());

        Assert.True(decision.Passed);
        Assert.Equal("Viewer One", decision.SafeAuthorDisplayName);
        Assert.Equal("Viewer One on TikTok LIVE said: hello stream", decision.SpokenText);
    }

    [Fact]
    public void Decoder_AnnouncesCompletedGiftStreakOnceWithTotal()
    {
        var decoder = new TikTokEventDecoder();

        Assert.Null(decoder.Decode("WebcastGiftMessage", Gift(repeat: 2, ended: false),
            "10", 61, false, DateTimeOffset.UtcNow));
        LivestreamEvent? completed = decoder.Decode("WebcastGiftMessage", Gift(repeat: 3, ended: true),
            "10", 62, false, DateTimeOffset.UtcNow);
        LivestreamEvent? repeated = decoder.Decode("WebcastGiftMessage", Gift(repeat: 3, ended: true),
            "10", 63, false, DateTimeOffset.UtcNow);

        Assert.NotNull(completed);
        Assert.Equal(LivestreamEventType.Gift, completed.Type);
        Assert.Equal("Rose", completed.GiftName);
        Assert.Equal(3, completed.GiftCount);
        Assert.Equal(1, completed.DiamondCount);
        Assert.Null(repeated);
    }

    [Fact]
    public async Task Connector_ReportsConnectedOnlyWhenSessionConfirmsAndStopsPromptly()
    {
        var session = new ControlledSession();
        await using var connector = new TikTokLiveConnector(
            "creator", () => session, TimeSpan.FromMilliseconds(1));
        var states = new ConcurrentQueue<ConnectionState>();
        var received = new TaskCompletionSource<LivestreamEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        connector.StateChanged += (_, value) => states.Enqueue(value.State);
        connector.EventReceived += (_, value) => received.TrySetResult(value);

        await connector.ConnectAsync();
        await session.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(ConnectionState.Connected, states);

        session.Confirm();
        LivestreamEvent liveEvent = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("test", liveEvent.Text);
        await connector.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ConnectionState.Disconnected, connector.State);
    }

    [Fact]
    public async Task MissingUsername_FaultsWithoutStartingNetworkSession()
    {
        bool created = false;
        await using var connector = new TikTokLiveConnector(
            "", () => { created = true; return new ControlledSession(); }, TimeSpan.Zero);

        await connector.ConnectAsync();

        Assert.Equal(ConnectionState.Faulted, connector.State);
        Assert.False(created);
    }

    [Fact]
    public void RoomParser_RequiresAnActivePublicRoom()
    {
        Assert.Equal("12345", TikTokLiveSession.ParseRoomResponse(
            """{"statusCode":0,"data":{"user":{"roomId":"12345"},"liveRoom":{"status":2}}}"""u8.ToArray()));
        TikTokConnectionException error = Assert.Throws<TikTokConnectionException>(() =>
            TikTokLiveSession.ParseRoomResponse(
                """{"statusCode":0,"data":{"user":{"roomId":"0"},"liveRoom":{"status":4}}}"""u8.ToArray()));
        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task ViewingCookie_IsRequestedFromCreatorLivePage()
    {
        var handler = new RecordingHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Set-Cookie", "ttwid=viewer-cookie; Path=/; Secure");
            return response;
        });
        using var http = new HttpClient(handler);

        string cookie = await TikTokLiveSession.GetCookieAsync(http, "safe_speak", CancellationToken.None);

        Assert.Equal("https://www.tiktok.com/@safe_speak/live", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("ttwid=viewer-cookie", cookie);
    }

    [Fact]
    public void Decompression_RejectsExpansionPastLimit()
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(new byte[TikTokLiveSession.MaximumDecodedBytes + 1]);
        }

        Assert.Throws<InvalidDataException>(() => TikTokLiveSession.Decompress(compressed.ToArray()));
    }

    private static byte[] Chat(string author, string name, string text, ulong id)
    {
        byte[] common = new TikTokProtobuf.Writer().Number(2, id)
            .Number(4, (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()).Build();
        byte[] user = new TikTokProtobuf.Writer().Number(1, 7).Text(3, name).Text(38, author).Build();
        return new TikTokProtobuf.Writer().Bytes(1, common).Bytes(2, user).Text(3, text).Build();
    }

    private static byte[] Gift(int repeat, bool ended)
    {
        byte[] common = new TikTokProtobuf.Writer()
            .Number(4, (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()).Build();
        byte[] user = new TikTokProtobuf.Writer().Number(1, 8).Text(3, "Gift Viewer").Text(38, "gift_viewer").Build();
        byte[] gift = new TikTokProtobuf.Writer().Number(11, 1).Number(12, 1).Text(16, "Rose").Build();
        return new TikTokProtobuf.Writer().Bytes(1, common).Number(2, 5655).Number(5, (ulong)repeat)
            .Bytes(7, user).Number(9, ended ? 1UL : 0UL).Number(11, 999).Bytes(15, gift).Build();
    }

    private sealed class ControlledSession : ITikTokLiveSession
    {
        private readonly TaskCompletionSource _confirm = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Confirm() => _confirm.TrySetResult();

        public async Task<bool> RunAsync(string username, TikTokEventDecoder decoder, Action connected,
            Action<LivestreamEvent> received, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await _confirm.Task.WaitAsync(cancellationToken);
            connected();
            received(new LivestreamEvent
            {
                Platform = "TikTok LIVE", Type = LivestreamEventType.Chat,
                Author = "viewer", AuthorDisplayName = "Viewer", Text = "test"
            });
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class AlwaysSafeIntentClassifier : IIntentClassifier
    {
        public string ModelName => "Test safe classifier";
        public bool IsModelLoaded => true;

        public Task<IntentClassificationResult> ClassifyAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IntentClassificationResult());

        public void Dispose()
        {
        }
    }
}
