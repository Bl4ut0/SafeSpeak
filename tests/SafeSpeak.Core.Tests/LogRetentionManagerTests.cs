using SafeSpeak.Core.Logging;

namespace SafeSpeak.Core.Tests;

public sealed class LogRetentionManagerTests : IDisposable
{
    private readonly string _testDirectory;

    public LogRetentionManagerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SafeSpeak_RetentionTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void CleanOldLogs_DeletesStreamAuditLogsOlderThanRetentionPeriod()
    {
        DateTimeOffset now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

        // 17 days old (expired)
        string expiredAudit1 = Path.Combine(_testDirectory, "StreamAudit_2026-08-25_12-00-00.log");
        File.WriteAllText(expiredAudit1, "old audit log");

        // 12 days old (expired)
        string expiredAudit2 = Path.Combine(_testDirectory, "StreamAudit_2026-08-30_19-42-39-364_0001.log");
        File.WriteAllText(expiredAudit2, "old audit log 2");

        // 3 days old (retained)
        string recentAudit = Path.Combine(_testDirectory, "StreamAudit_2026-09-08_15-30-00-100_0001.log");
        File.WriteAllText(recentAudit, "recent audit log");

        // Today (retained)
        string todayAudit = Path.Combine(_testDirectory, "StreamAudit_2026-09-11_08-00-00-200_0001.log");
        File.WriteAllText(todayAudit, "today audit log");

        int deleted = LogRetentionManager.CleanOldLogs(_testDirectory, retentionDays: 10, referenceTime: now);

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(expiredAudit1));
        Assert.False(File.Exists(expiredAudit2));
        Assert.True(File.Exists(recentAudit));
        Assert.True(File.Exists(todayAudit));
    }

    [Fact]
    public void CleanOldLogs_DeletesArchivedDebugLogsOlderThanRetentionPeriod()
    {
        DateTimeOffset now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

        // 20 days old (expired)
        string expiredDebug = Path.Combine(_testDirectory, "debug_2026-08-22.log");
        File.WriteAllText(expiredDebug, "old debug");

        // 15 days old with timestamp (expired)
        string expiredTimestamped = Path.Combine(_testDirectory, "debug_2026-08-27_10-15-30.log");
        File.WriteAllText(expiredTimestamped, "old timestamped debug");

        // 5 days old (retained)
        string recentDebug = Path.Combine(_testDirectory, "debug_2026-09-06.log");
        File.WriteAllText(recentDebug, "recent debug");

        // Active debug.log (must never be deleted directly)
        string activeDebug = Path.Combine(_testDirectory, "debug.log");
        File.WriteAllText(activeDebug, "active debug");

        int deleted = LogRetentionManager.CleanOldLogs(_testDirectory, retentionDays: 10, referenceTime: now);

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(expiredDebug));
        Assert.False(File.Exists(expiredTimestamped));
        Assert.True(File.Exists(recentDebug));
        Assert.True(File.Exists(activeDebug));
    }

    [Fact]
    public void CleanOldLogs_DoesNotTouchNonCandidateFiles()
    {
        DateTimeOffset now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

        string textFile = Path.Combine(_testDirectory, "notes_2026-08-01.txt");
        File.WriteAllText(textFile, "keep me");

        string jsonFile = Path.Combine(_testDirectory, "config.json");
        File.WriteAllText(jsonFile, "keep me too");

        int deleted = LogRetentionManager.CleanOldLogs(_testDirectory, retentionDays: 10, referenceTime: now);

        Assert.Equal(0, deleted);
        Assert.True(File.Exists(textFile));
        Assert.True(File.Exists(jsonFile));
    }

    [Theory]
    [InlineData("StreamAudit_2026-09-10_18-29-02-205_0001.log", 2026, 9, 10)]
    [InlineData("debug_2026-09-01.log", 2026, 9, 1)]
    [InlineData("debug_2026-08-25_14-30-00.log", 2026, 8, 25)]
    [InlineData("StreamAudit_2026-12-31.log", 2026, 12, 31)]
    public void TryExtractDateFromFileName_ParsesCorrectly(string fileName, int year, int month, int day)
    {
        bool success = LogRetentionManager.TryExtractDateFromFileName(fileName, out DateTime parsed);
        Assert.True(success);
        Assert.Equal(new DateTime(year, month, day), parsed.Date);
    }

    [Theory]
    [InlineData("debug.log")]
    [InlineData("debug.old.log")]
    [InlineData("random_file.log")]
    [InlineData("StreamAudit.log")]
    public void TryExtractDateFromFileName_ReturnsFalseForNonMatching(string fileName)
    {
        bool success = LogRetentionManager.TryExtractDateFromFileName(fileName, out _);
        Assert.False(success);
    }
}
