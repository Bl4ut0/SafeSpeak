using SafeSpeak.Core.Logging;

namespace SafeSpeak.Core.Tests;

public sealed class AppLoggerTests : IDisposable
{
    private readonly string _testDirectory;

    public AppLoggerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SafeSpeak_AppLoggerTest_" + Guid.NewGuid().ToString("N"));
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
    public async Task AppLogger_CapturesEntriesInRingBuffer()
    {
        await using var logger = new AppLogger(_testDirectory, ringBufferCapacity: 10, writeToFile: false);

        logger.Log(AppLogLevel.Information, "TestSrc", "Message 1");
        logger.Log(AppLogLevel.Warning, "TestSrc", "Message 2");
        logger.Log(AppLogLevel.Error, "TestSrc", "Message 3");

        var snapshot = logger.GetRecentSnapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal("Message 1", snapshot[0].Message);
        Assert.Equal(AppLogLevel.Information, snapshot[0].Level);
        Assert.Equal("Message 2", snapshot[1].Message);
        Assert.Equal(AppLogLevel.Warning, snapshot[1].Level);
        Assert.Equal("Message 3", snapshot[2].Message);
        Assert.Equal(AppLogLevel.Error, snapshot[2].Level);
    }

    [Fact]
    public async Task AppLogger_RespectsRingBufferCapacity()
    {
        await using var logger = new AppLogger(_testDirectory, ringBufferCapacity: 3, writeToFile: false);

        for (int i = 1; i <= 5; i++)
        {
            logger.Log(AppLogLevel.Information, "Source", $"Entry {i}");
        }

        var snapshot = logger.GetRecentSnapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal("Entry 3", snapshot[0].Message);
        Assert.Equal("Entry 4", snapshot[1].Message);
        Assert.Equal("Entry 5", snapshot[2].Message);
    }

    [Fact]
    public async Task AppLogger_FiresEntryLoggedEvents()
    {
        await using var logger = new AppLogger(_testDirectory, ringBufferCapacity: 10, writeToFile: false);
        var logged = new List<AppLogEntry>();
        logger.EntryLogged += entry => logged.Add(entry);

        logger.Log(AppLogLevel.Information, "EventSource", "Event message");

        Assert.Single(logged);
        Assert.Equal("EventSource", logged[0].Source);
        Assert.Equal("Event message", logged[0].Message);
    }

    [Fact]
    public async Task AppLogger_WritesToFileAndRotates()
    {
        await using var logger = new AppLogger(_testDirectory, maxFileSizeBytes: 500, ringBufferCapacity: 10, writeToFile: true);

        for (int i = 0; i < 20; i++)
        {
            logger.Log(AppLogLevel.Information, "RotateTest", new string('A', 80) + $" index {i}");
        }

        // Wait for background worker to flush and dispose
        await logger.DisposeAsync();

        Assert.True(File.Exists(logger.LogFilePath));
        string logContent = await File.ReadAllTextAsync(logger.LogFilePath);
        Assert.NotEmpty(logContent);

        string oldLogPath = Path.Combine(_testDirectory, "debug.old.log");
        Assert.True(File.Exists(oldLogPath));
        string oldContent = await File.ReadAllTextAsync(oldLogPath);
        Assert.NotEmpty(oldContent);
    }

    [Fact]
    public void AppLogger_StaticMethods_DoNotThrow()
    {
        AppLogger.LogDebug("StaticTest", "Debug message");
        AppLogger.LogInformation("StaticTest", "Info message");
        AppLogger.LogWarning("StaticTest", "Warning message", new InvalidOperationException("test warn"));
        AppLogger.LogError("StaticTest", "Error message", new Exception("test err"));

        var recent = AppLogger.GetRecentEntries();
        Assert.NotEmpty(recent);
    }
}
