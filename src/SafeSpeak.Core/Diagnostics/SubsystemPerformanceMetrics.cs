using System.Collections.Concurrent;
using System.Diagnostics;

namespace SafeSpeak.Core.Diagnostics;

public sealed record SubsystemMetricSnapshot(
    string Name,
    long OperationCount,
    long FailureCount,
    double AverageMilliseconds,
    double MaximumMilliseconds,
    double LastMilliseconds);

/// <summary>
/// Records bounded, process-local operation timings for trend diagnostics.
/// Values are cumulative for the current SafeSpeak session so adjacent periodic
/// snapshots can be compared without retaining individual chat events.
/// </summary>
public static class SubsystemPerformanceMetrics
{
    private static readonly ConcurrentDictionary<string, Accumulator> s_metrics =
        new(StringComparer.Ordinal);

    public static OperationTimer Measure(string subsystem) => new(subsystem);

    public static IReadOnlyList<SubsystemMetricSnapshot> Capture() =>
        s_metrics.ToArray()
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value.Snapshot(pair.Key))
            .ToArray();

    internal static void ResetForTests() => s_metrics.Clear();

    public sealed class OperationTimer : IDisposable
    {
        private readonly string _subsystem;
        private readonly long _started = Stopwatch.GetTimestamp();
        private int _failed;
        private int _disposed;

        internal OperationTimer(string subsystem)
        {
            if (string.IsNullOrWhiteSpace(subsystem))
            {
                throw new ArgumentException("A subsystem name is required.", nameof(subsystem));
            }

            _subsystem = subsystem.Trim();
        }

        public void MarkFailed() => Interlocked.Exchange(ref _failed, 1);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            long elapsed = Stopwatch.GetTimestamp() - _started;
            s_metrics.GetOrAdd(_subsystem, static _ => new Accumulator())
                .Record(elapsed, Volatile.Read(ref _failed) != 0);
        }
    }

    private sealed class Accumulator
    {
        private long _count;
        private long _failures;
        private long _totalTicks;
        private long _maximumTicks;
        private long _lastTicks;

        public void Record(long elapsedTicks, bool failed)
        {
            Interlocked.Increment(ref _count);
            if (failed) Interlocked.Increment(ref _failures);
            Interlocked.Add(ref _totalTicks, elapsedTicks);
            Interlocked.Exchange(ref _lastTicks, elapsedTicks);

            long currentMaximum = Volatile.Read(ref _maximumTicks);
            while (elapsedTicks > currentMaximum)
            {
                long observed = Interlocked.CompareExchange(
                    ref _maximumTicks,
                    elapsedTicks,
                    currentMaximum);
                if (observed == currentMaximum) break;
                currentMaximum = observed;
            }
        }

        public SubsystemMetricSnapshot Snapshot(string name)
        {
            long count = Volatile.Read(ref _count);
            long total = Volatile.Read(ref _totalTicks);
            return new SubsystemMetricSnapshot(
                name,
                count,
                Volatile.Read(ref _failures),
                count == 0 ? 0 : ToMilliseconds(total) / count,
                ToMilliseconds(Volatile.Read(ref _maximumTicks)),
                ToMilliseconds(Volatile.Read(ref _lastTicks)));
        }

        private static double ToMilliseconds(long ticks) =>
            ticks * 1000.0 / Stopwatch.Frequency;
    }
}
