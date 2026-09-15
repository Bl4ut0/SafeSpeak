using System.Diagnostics;
using SafeSpeak.Core.Diagnostics;

namespace SafeSpeak.Core.Tests;

public sealed class PerformanceMetricsTests
{
    [Fact]
    public void PerformanceSnapshot_ToLogString_FormatsCompleteMetrics()
    {
        var snapshot = new PerformanceSnapshot
        {
            CpuUsagePercent = 2.5,
            TotalProcessorTime = TimeSpan.FromSeconds(15.25),
            WorkingSetMb = 85.5,
            PeakWorkingSetMb = 95.0,
            PrivateBytesMb = 90.2,
            ManagedHeapMb = 24.8,
            Gen0Collections = 12,
            Gen1Collections = 4,
            Gen2Collections = 1,
            ThreadCount = 28,
            AppState = new AppStateSnapshot
            {
                IsArmed = true,
                PlaybackMode = "Paused",
                IsPaused = true,
                IsSpeaking = false,
                TtsQueueCount = 2,
                AlertQueueCount = 1,
                LiveFeedCount = 15,
                ConnectorsStatus = "1 of 1 connected",
                ChildProcessInfo = "Running (PID=1234, RAM=120.0MB)"
            }
        };

        string formatted = snapshot.ToLogString("TriggerReason");

        Assert.Contains("[TriggerReason]", formatted);
        Assert.Contains("CPU=2.5%", formatted);
        Assert.Contains("WorkingSet=85.5MB", formatted);
        Assert.Contains("PeakWS=95.0MB", formatted);
        Assert.Contains("PrivateBytes=90.2MB", formatted);
        Assert.Contains("ManagedHeap=24.8MB", formatted);
        Assert.Contains("GC=[G0:12, G1:4, G2:1]", formatted);
        Assert.Contains("Threads=28", formatted);
        Assert.Contains("Armed=True", formatted);
        Assert.Contains("Mode=Paused", formatted);
        Assert.Contains("Paused=True", formatted);
        Assert.Contains("TTS=2", formatted);
        Assert.Contains("Alert=1", formatted);
        Assert.Contains("LiveFeed=15", formatted);
        Assert.Contains("ChildProc=[Running (PID=1234, RAM=120.0MB)]", formatted);
    }

    [Fact]
    public void PerformanceTracker_Capture_ReturnsRealisticProcessMetrics()
    {
        using var tracker = new PerformanceTracker();
        var snapshot = tracker.Capture();

        Assert.True(snapshot.WorkingSetMb > 0, "WorkingSetMb should be greater than zero.");
        Assert.True(snapshot.ManagedHeapMb > 0, "ManagedHeapMb should be greater than zero.");
        Assert.True(snapshot.ThreadCount > 0, "ThreadCount should be greater than zero.");
        Assert.True(snapshot.Gen0Collections >= 0);
        Assert.True(snapshot.Gen1Collections >= 0);
        Assert.True(snapshot.Gen2Collections >= 0);
    }

    [Fact]
    public void PerformanceTracker_LogSnapshot_InvokesAppStateProvider()
    {
        using var tracker = new PerformanceTracker();
        bool providerCalled = false;

        PerformanceTracker.AppStateProvider = () =>
        {
            providerCalled = true;
            return new AppStateSnapshot
            {
                IsArmed = true,
                PlaybackMode = "Automatic",
                IsPaused = false,
                IsSpeaking = true,
                TtsQueueCount = 5,
                AlertQueueCount = 0,
                LiveFeedCount = 20,
                ConnectorsStatus = "TikTok: Connected",
                ChildProcessInfo = "None"
            };
        };

        try
        {
            var snapshot = tracker.RecordAndLog("TestEvent");

            Assert.True(providerCalled);
            Assert.NotNull(snapshot.AppState);
            Assert.True(snapshot.AppState.IsArmed);
            Assert.Equal("Automatic", snapshot.AppState.PlaybackMode);
            Assert.Equal(5, snapshot.AppState.TtsQueueCount);
        }
        finally
        {
            PerformanceTracker.AppStateProvider = null;
        }
    }

    [Fact]
    public void PerformanceTracker_StartAndStop_ManagesTimerState()
    {
        using var tracker = new PerformanceTracker(TimeSpan.FromSeconds(10));
        Assert.False(tracker.IsRunning);

        tracker.Start();
        Assert.True(tracker.IsRunning);

        tracker.Stop();
        Assert.False(tracker.IsRunning);
    }

    [Fact]
    public void JobObjectManager_CanBeCreated_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var job = new JobObjectManager();
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 3",
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using var childProcess = Process.Start(startInfo);
        Assert.NotNull(childProcess);
        try
        {
            bool assigned = job.AssignProcess(childProcess);
            Assert.True(assigned);
        }
        finally
        {
            try { childProcess.Kill(); } catch { }
        }
    }
}
