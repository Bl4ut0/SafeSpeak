using SafeSpeak.Core.Logging;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Tests;

public sealed class StreamAuditLoggerTests : IDisposable
{
    private readonly string _testDirectory;

    public StreamAuditLoggerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SafeSpeak_AuditTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task StartSession_CreatesTimestampedLogWithHeader()
    {
        var logger = new StreamAuditLogger(_testDirectory) { IsEnabled = true };
        var config = new ModerationConfig { AudienceMode = AudienceMode.All, Strictness = ModerationStrictness.High };

        Assert.True(logger.StartSession("TikFinity", "ws://localhost:21213/", config));
        string path = Assert.IsType<string>(logger.CurrentLogFilePath);
        await logger.FlushAsync();
        Assert.True(File.Exists(path));

        await logger.DisposeAsync();
        string content = await File.ReadAllTextAsync(path);
        Assert.Contains("SAFESPEAK STREAM AUDIT LOG", content);
        Assert.Contains("TikFinity (ws://localhost:21213/)", content);
        Assert.Contains("NOTE: All raw messages are logged unfiltered", content);
    }

    [Fact]
    public async Task LogDecision_WritesUnfilteredMessageAndDecision()
    {
        var logger = new StreamAuditLogger(_testDirectory) { IsEnabled = true };
        logger.StartSession("TikFinity", "ws://localhost:21213/");
        string path = Assert.IsType<string>(logger.CurrentLogFilePath);
        var message = new ChatMessage
        {
            Author = "troll_user",
            AuthorDisplayName = "Viewer Display Name",
            RawText = "pау mе monеy now",
            AuthorTier = AuthorTier.Viewer,
            TimestampUtc = DateTimeOffset.UtcNow
        };
        var decision = new ModerationDecision
        {
            Message = message,
            Disposition = ModerationDisposition.Rejected,
            ReasonCode = ModerationReasonCode.DisallowedScript,
            ReasonDescription = "Mixed writing systems detected in single word",
            NormalizedText = "pay me money now",
            SpokenText = string.Empty,
            ToxicityScore = 0.25,
            TriggeredRules = new[] { "HomoglyphSpoofing" }
        };

        logger.LogDecision(message, decision);
        await logger.DisposeAsync();

        string content = await File.ReadAllTextAsync(path);
        Assert.Contains("@troll_user", content);
        Assert.Contains("RAW UNFILTERED: \"pау mе monеy now\"", content);
        Assert.Contains("NORMALIZED:     \"pay me money now\"", content);
        Assert.Contains("DISPOSITION:    REJECTED (DisallowedScript: Mixed writing systems detected in single word)", content);
        Assert.Contains("TOXICITY SCORE: 0.25", content);
        Assert.Contains("TRIGGERED:      HomoglyphSpoofing", content);
        Assert.Contains("SPOKEN OUTPUT:  <SILENT/BLOCKED>", content);
    }

    [Fact]
    public async Task LogEvent_WritesGiftAndSummaryCount()
    {
        var logger = new StreamAuditLogger(_testDirectory) { IsEnabled = true };
        logger.StartSession("TikFinity", "provider");
        string path = Assert.IsType<string>(logger.CurrentLogFilePath);
        logger.LogEvent(new LivestreamEvent
        {
            Type = LivestreamEventType.Gift,
            Author = "generous_fan",
            AuthorDisplayName = "Fan Sarah",
            GiftName = "Galaxy",
            GiftCount = 3,
            TimestampUtc = DateTimeOffset.UtcNow,
            IsSubscriber = true
        }, null);

        await logger.DisposeAsync();
        string content = await File.ReadAllTextAsync(path);
        Assert.Contains("[GIFT]", content);
        Assert.Contains("[SUB] @generous_fan", content);
        Assert.Contains("GIFT DETAILS:   3x Galaxy", content);
        Assert.Contains("Total Gifts Logged:     1", content);
    }

    [Fact]
    public async Task Dispose_FlushesSessionSummaryCounts()
    {
        var logger = new StreamAuditLogger(_testDirectory) { IsEnabled = true };
        logger.StartSession("TikFinity", "provider");
        string path = Assert.IsType<string>(logger.CurrentLogFilePath);
        var approved = Message("Hello");
        var rejected = Message("Bad text");
        logger.LogDecision(approved, Decision(approved, ModerationDisposition.Approved));
        logger.LogDecision(rejected, Decision(rejected, ModerationDisposition.Rejected));

        Assert.Equal(2, logger.TotalMessages);
        Assert.Equal(1, logger.TotalApproved);
        Assert.Equal(1, logger.TotalRejected);

        await logger.DisposeAsync();
        string content = await File.ReadAllTextAsync(path);
        Assert.Contains("Total Messages:         2", content);
        Assert.Contains("Approved:           1", content);
        Assert.Contains("Rejected:           1", content);
        Assert.Contains("Dropped Records:        0", content);
    }

    [Fact]
    public async Task EnabledBeforeConnection_DiscardsRecordsUntilSessionStarts()
    {
        var logger = new StreamAuditLogger(_testDirectory) { IsEnabled = true };
        var before = Message("before connection");
        logger.LogDecision(before, Decision(before));
        await logger.FlushAsync();

        Assert.Empty(Directory.GetFiles(_testDirectory));
        Assert.Equal(0, logger.TotalMessages);

        Assert.True(logger.StartSession("TikFinity", "provider"));
        string path = Assert.IsType<string>(logger.CurrentLogFilePath);
        var after = Message("after connection");
        logger.LogDecision(after, Decision(after));
        await logger.DisposeAsync();

        Assert.Contains("after connection", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task EnableMidstream_RequiresCoordinatorToStartConnectedSession()
    {
        var logger = new StreamAuditLogger(_testDirectory);
        Assert.False(logger.StartSession("TikFinity", "provider"));

        logger.IsEnabled = true;
        Assert.False(logger.HasActiveSession);
        Assert.Empty(Directory.GetFiles(_testDirectory));

        Assert.True(logger.StartSession("TikFinity", "provider"));
        string path = Assert.IsType<string>(logger.CurrentLogFilePath);
        var message = Message("midstream enabled");
        logger.LogDecision(message, Decision(message));
        await logger.DisposeAsync();

        Assert.Contains("midstream enabled", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task DisableMidstream_EndsSessionAndRejectsLaterRecords()
    {
        var logger = new StreamAuditLogger(_testDirectory) { IsEnabled = true };
        logger.StartSession("TikFinity", "provider");
        string path = Assert.IsType<string>(logger.CurrentLogFilePath);
        var before = Message("before disable");
        logger.LogDecision(before, Decision(before));

        logger.IsEnabled = false;
        Assert.False(logger.HasActiveSession);
        var after = Message("after disable");
        logger.LogDecision(after, Decision(after));
        await logger.FlushAsync();
        await logger.DisposeAsync();

        string content = await File.ReadAllTextAsync(path);
        Assert.Contains("before disable", content);
        Assert.DoesNotContain("after disable", content);
        Assert.Contains("Stream Session Ended", content);
    }

    [Fact]
    public async Task Reconnect_ClosesOldSessionAndRoutesToDistinctFile()
    {
        var logger = new StreamAuditLogger(_testDirectory) { IsEnabled = true };
        logger.StartSession("TikFinity", "first");
        string firstPath = Assert.IsType<string>(logger.CurrentLogFilePath);
        var first = Message("first session only");
        logger.LogDecision(first, Decision(first));

        logger.StartSession("TikFinity", "second");
        string secondPath = Assert.IsType<string>(logger.CurrentLogFilePath);
        var second = Message("second session only");
        logger.LogDecision(second, Decision(second));
        logger.EndSession();
        await logger.FlushAsync();
        await logger.DisposeAsync();

        Assert.NotEqual(firstPath, secondPath);
        string firstContent = await File.ReadAllTextAsync(firstPath);
        string secondContent = await File.ReadAllTextAsync(secondPath);
        Assert.Contains("first session only", firstContent);
        Assert.DoesNotContain("second session only", firstContent);
        Assert.Contains("Stream Session Ended", firstContent);
        Assert.Contains("second session only", secondContent);
        Assert.DoesNotContain("first session only", secondContent);
        Assert.Contains("Stream Session Ended", secondContent);
    }

    [Fact]
    public async Task InvalidLogDirectory_FailsSafelyWithoutThrowing()
    {
        string fileInsteadOfDirectory = Path.Combine(_testDirectory, "not-a-directory");
        await File.WriteAllTextAsync(fileInsteadOfDirectory, "occupied");
        var logger = new StreamAuditLogger(fileInsteadOfDirectory) { IsEnabled = true };

        Assert.True(logger.StartSession("TikFinity", "provider"));
        await logger.FlushAsync();

        Assert.False(logger.HasActiveSession);
        Assert.NotNull(logger.LastError);
        var message = Message("must not throw");
        logger.LogDecision(message, Decision(message));
        await logger.DisposeAsync();
    }

    [Fact]
    public async Task BoundedBurst_DropsRecordsWithoutDroppingLifecycleCommands()
    {
        var logger = new StreamAuditLogger(_testDirectory, queueCapacity: 1) { IsEnabled = true };
        logger.StartSession("TikFinity", "provider");
        string path = Assert.IsType<string>(logger.CurrentLogFilePath);

        for (int index = 0; index < 10_000; index++)
        {
            var message = Message($"burst {index}");
            logger.LogDecision(message, Decision(message));
        }

        logger.EndSession();
        await logger.FlushAsync();
        await logger.DisposeAsync();

        Assert.True(logger.DroppedRecordCount > 0);
        string content = await File.ReadAllTextAsync(path);
        Assert.Contains("Stream Session Ended", content);
        Assert.Contains("Dropped Records:", content);
    }

    [Fact]
    public async Task EndSession_IsIdempotentAndDisposeCompletesPromptly()
    {
        var logger = new StreamAuditLogger(_testDirectory) { IsEnabled = true };
        logger.StartSession("TikFinity", "provider");
        string path = Assert.IsType<string>(logger.CurrentLogFilePath);
        var message = Message("flush on close");
        logger.LogDecision(message, Decision(message));

        logger.EndSession();
        logger.EndSession();
        await logger.FlushAsync();

        Task dispose = logger.DisposeAsync().AsTask();
        await dispose.WaitAsync(TimeSpan.FromSeconds(3));
        string content = await File.ReadAllTextAsync(path);
        Assert.Contains("flush on close", content);
        Assert.Contains("Stream Session Ended", content);
    }

    private static ChatMessage Message(string text) => new()
    {
        Author = "viewer",
        RawText = text,
        TimestampUtc = DateTimeOffset.UtcNow
    };

    private static ModerationDecision Decision(
        ChatMessage message,
        ModerationDisposition disposition = ModerationDisposition.Approved) => new()
    {
        Message = message,
        Disposition = disposition,
        NormalizedText = message.RawText,
        SpokenText = disposition == ModerationDisposition.Approved ? message.RawText : string.Empty
    };
}
