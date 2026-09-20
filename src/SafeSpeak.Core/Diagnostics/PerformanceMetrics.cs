using System.Globalization;
using System.Text;

namespace SafeSpeak.Core.Diagnostics;

/// <summary>
/// Point-in-time snapshot of SafeSpeak's domain and subsystem state for diagnostic logging.
/// </summary>
public sealed record AppStateSnapshot
{
    public bool IsArmed { get; init; }
    public string PlaybackMode { get; init; } = "Disarmed";
    public bool IsPaused { get; init; }
    public bool IsSpeaking { get; init; }
    public int TtsQueueCount { get; init; }
    public int TtsQueueCapacity { get; init; }
    public double OldestTtsMessageSeconds { get; init; }
    public double ActiveSpeechSeconds { get; init; }
    public int AlertQueueCount { get; init; }
    public int LiveFeedCount { get; init; }
    public string ConnectorsStatus { get; init; } = "None";
    public string ChildProcessInfo { get; init; } = "None";
}

/// <summary>
/// Structured system, process, and application performance metrics for diagnostic logging.
/// </summary>
public sealed record PerformanceSnapshot
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public double CpuUsagePercent { get; init; }
    public double SystemCpuUsagePercent { get; init; }
    public double SafeSpeakShareOfBusyCpuPercent { get; init; }
    public TimeSpan TotalProcessorTime { get; init; }
    public double WorkingSetMb { get; init; }
    public double PeakWorkingSetMb { get; init; }
    public double PrivateBytesMb { get; init; }
    public double ManagedHeapMb { get; init; }
    public double AvailablePhysicalMemoryMb { get; init; }
    public double TotalPhysicalMemoryMb { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public int ThreadCount { get; init; }
    public AppStateSnapshot? AppState { get; init; }
    public IReadOnlyList<SubsystemMetricSnapshot> Subsystems { get; init; } = [];

    public string ToLogString(string? trigger = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(trigger))
        {
            sb.Append('[').Append(trigger).Append("] ");
        }

        sb.Append(CultureInfo.InvariantCulture,
            $"ProcessCPU={CpuUsagePercent:F1}%, SystemCPU={SystemCpuUsagePercent:F1}%, SafeSpeakShareOfBusyCPU={SafeSpeakShareOfBusyCpuPercent:F1}% (Total={TotalProcessorTime:hh\\:mm\\:ss\\.ff}), WorkingSet={WorkingSetMb:F1}MB, PeakWS={PeakWorkingSetMb:F1}MB, PrivateBytes={PrivateBytesMb:F1}MB, ManagedHeap={ManagedHeapMb:F1}MB, MemoryAvailable={AvailablePhysicalMemoryMb:F0}MB/{TotalPhysicalMemoryMb:F0}MB, GC=[G0:{Gen0Collections}, G1:{Gen1Collections}, G2:{Gen2Collections}], Threads={ThreadCount}");

        if (AppState is not null)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $", State=[Armed={AppState.IsArmed}, Mode={AppState.PlaybackMode}, Paused={AppState.IsPaused}, Speaking={AppState.IsSpeaking}], Queues=[TTS={AppState.TtsQueueCount}, Capacity={AppState.TtsQueueCapacity}, OldestMessageSeconds={AppState.OldestTtsMessageSeconds:F1}, ActiveSpeechSeconds={AppState.ActiveSpeechSeconds:F1}, Alert={AppState.AlertQueueCount}, LiveFeed={AppState.LiveFeedCount}], Connectors=[{AppState.ConnectorsStatus}], ChildProc=[{AppState.ChildProcessInfo}]");
        }

        if (Subsystems.Count > 0)
        {
            sb.Append(", Subsystems=[");
            sb.Append(string.Join("; ", Subsystems.Select(metric =>
                FormattableString.Invariant(
                    $"{metric.Name}:Count={metric.OperationCount},Avg={metric.AverageMilliseconds:F1}ms,Max={metric.MaximumMilliseconds:F1}ms,Last={metric.LastMilliseconds:F1}ms,Failures={metric.FailureCount}"))));
            sb.Append(']');
        }

        return sb.ToString();
    }

    public override string ToString() => ToLogString();
}
