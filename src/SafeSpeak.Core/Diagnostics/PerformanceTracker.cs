using System.Diagnostics;
using System.Runtime.InteropServices;
using SafeSpeak.Core.Logging;

namespace SafeSpeak.Core.Diagnostics;

/// <summary>
/// Lightweight, non-blocking diagnostic tracker that periodically samples system and
/// process resources, and provides on-demand snapshots during lifecycle events.
/// </summary>
public sealed class PerformanceTracker : IDisposable
{
    private static PerformanceTracker s_instance = new();
    public static PerformanceTracker Instance
    {
        get => Volatile.Read(ref s_instance);
        set => Volatile.Write(ref s_instance, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>
    /// Optional provider registered by the application layer to supply domain-specific state.
    /// </summary>
    public static Func<AppStateSnapshot>? AppStateProvider { get; set; }

    public static PerformanceSnapshot LogSnapshot(string triggerReason, AppStateSnapshot? explicitAppState = null) =>
        Instance.RecordAndLog(triggerReason, explicitAppState);

    private readonly object _sampleLock = new();
    private readonly TimeSpan _sampleInterval;
    private Timer? _timer;
    private Process? _currentProcess;
    private DateTimeOffset _lastSampleTimeUtc = DateTimeOffset.UtcNow;
    private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;
    private ulong _lastSystemIdleTime;
    private ulong _lastSystemKernelTime;
    private ulong _lastSystemUserTime;
    private bool _disposed;

    public TimeSpan SampleInterval => _sampleInterval;
    public bool IsRunning => _timer is not null;

    public PerformanceTracker(TimeSpan? sampleInterval = null)
    {
        _sampleInterval = sampleInterval ?? TimeSpan.FromSeconds(60);
        try
        {
            _currentProcess = Process.GetCurrentProcess();
            _lastTotalProcessorTime = _currentProcess.TotalProcessorTime;
            _lastSampleTimeUtc = DateTimeOffset.UtcNow;
            CaptureSystemCpuBaseline();
        }
        catch
        {
            _lastTotalProcessorTime = TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Starts periodic background sampling and logging.
    /// </summary>
    public void Start()
    {
        lock (_sampleLock)
        {
            if (_disposed || _timer is not null)
            {
                return;
            }

            _timer = new Timer(
                OnTimerTick,
                null,
                _sampleInterval,
                _sampleInterval);
        }
    }

    /// <summary>
    /// Stops periodic background sampling.
    /// </summary>
    public void Stop()
    {
        lock (_sampleLock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void OnTimerTick(object? state)
    {
        try
        {
            RecordAndLog("Periodic");
        }
        catch
        {
            // Suppress background diagnostic exceptions
        }
    }

    /// <summary>
    /// Captures a current performance snapshot, logs it to AppLogger, and returns the snapshot.
    /// </summary>
    public PerformanceSnapshot RecordAndLog(string triggerReason, AppStateSnapshot? explicitAppState = null)
    {
        PerformanceSnapshot snapshot = Capture(explicitAppState);
        try
        {
            AppLogger.LogInformation("Performance", snapshot.ToLogString(triggerReason));
        }
        catch
        {
        }
        return snapshot;
    }

    /// <summary>
    /// Captures a performance snapshot without writing to the log.
    /// </summary>
    public PerformanceSnapshot Capture(AppStateSnapshot? explicitAppState = null)
    {
        double cpuPercent = 0.0;
        TimeSpan totalCpuTime = TimeSpan.Zero;
        double workingSetMb = 0.0;
        double peakWsMb = 0.0;
        double privateBytesMb = 0.0;
        double managedHeapMb = 0.0;
        double systemCpuPercent = 0.0;
        double availablePhysicalMemoryMb = 0.0;
        double totalPhysicalMemoryMb = 0.0;
        int g0 = 0, g1 = 0, g2 = 0;
        int threadCount = 0;

        try
        {
            managedHeapMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            g0 = GC.CollectionCount(0);
            g1 = GC.CollectionCount(1);
            g2 = GC.CollectionCount(2);

            lock (_sampleLock)
            {
                _currentProcess ??= Process.GetCurrentProcess();
                _currentProcess.Refresh();

                DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
                totalCpuTime = _currentProcess.TotalProcessorTime;
                workingSetMb = _currentProcess.WorkingSet64 / (1024.0 * 1024.0);
                peakWsMb = _currentProcess.PeakWorkingSet64 / (1024.0 * 1024.0);
                privateBytesMb = _currentProcess.PrivateMemorySize64 / (1024.0 * 1024.0);
                threadCount = _currentProcess.Threads.Count;

                double timeDeltaMs = (nowUtc - _lastSampleTimeUtc).TotalMilliseconds;
                double cpuDeltaMs = (totalCpuTime - _lastTotalProcessorTime).TotalMilliseconds;

                if (timeDeltaMs > 100 && Environment.ProcessorCount > 0)
                {
                    cpuPercent = Math.Clamp(
                        (cpuDeltaMs / (timeDeltaMs * Environment.ProcessorCount)) * 100.0,
                        0.0,
                        100.0);
                }

                systemCpuPercent = CaptureSystemCpuPercent();
                CapturePhysicalMemory(
                    out availablePhysicalMemoryMb,
                    out totalPhysicalMemoryMb);

                _lastSampleTimeUtc = nowUtc;
                _lastTotalProcessorTime = totalCpuTime;
            }
        }
        catch
        {
            // Best effort process metrics inspection
        }

        AppStateSnapshot? appState = explicitAppState;
        if (appState is null)
        {
            try
            {
                appState = AppStateProvider?.Invoke();
            }
            catch
            {
            }
        }

        return new PerformanceSnapshot
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            CpuUsagePercent = cpuPercent,
            SystemCpuUsagePercent = systemCpuPercent,
            SafeSpeakShareOfBusyCpuPercent = systemCpuPercent > 0.01
                ? Math.Clamp(cpuPercent / systemCpuPercent * 100.0, 0.0, 100.0)
                : 0.0,
            TotalProcessorTime = totalCpuTime,
            WorkingSetMb = workingSetMb,
            PeakWorkingSetMb = peakWsMb,
            PrivateBytesMb = privateBytesMb,
            ManagedHeapMb = managedHeapMb,
            AvailablePhysicalMemoryMb = availablePhysicalMemoryMb,
            TotalPhysicalMemoryMb = totalPhysicalMemoryMb,
            Gen0Collections = g0,
            Gen1Collections = g1,
            Gen2Collections = g2,
            ThreadCount = threadCount,
            AppState = appState,
            Subsystems = SubsystemPerformanceMetrics.Capture()
        };
    }

    private void CaptureSystemCpuBaseline()
    {
        if (!OperatingSystem.IsWindows() ||
            !GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
        {
            return;
        }

        _lastSystemIdleTime = idle.Value;
        _lastSystemKernelTime = kernel.Value;
        _lastSystemUserTime = user.Value;
    }

    private double CaptureSystemCpuPercent()
    {
        if (!OperatingSystem.IsWindows() ||
            !GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
        {
            return 0.0;
        }

        ulong idleDelta = idle.Value - _lastSystemIdleTime;
        ulong kernelDelta = kernel.Value - _lastSystemKernelTime;
        ulong userDelta = user.Value - _lastSystemUserTime;
        _lastSystemIdleTime = idle.Value;
        _lastSystemKernelTime = kernel.Value;
        _lastSystemUserTime = user.Value;
        ulong totalDelta = kernelDelta + userDelta;
        return totalDelta == 0
            ? 0.0
            : Math.Clamp((1.0 - idleDelta / (double)totalDelta) * 100.0, 0.0, 100.0);
    }

    private static void CapturePhysicalMemory(
        out double availablePhysicalMemoryMb,
        out double totalPhysicalMemoryMb)
    {
        availablePhysicalMemoryMb = 0.0;
        totalPhysicalMemoryMb = 0.0;
        if (!OperatingSystem.IsWindows()) return;

        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) return;
        availablePhysicalMemoryMb = status.AvailablePhysical / (1024.0 * 1024.0);
        totalPhysicalMemoryMb = status.TotalPhysical / (1024.0 * 1024.0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _currentProcess?.Dispose();
        _currentProcess = null;
    }
}
