using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace SafeSpeak.Core.Logging;

public enum AppLogLevel
{
    Debug,
    Information,
    Warning,
    Error
}

public sealed record AppLogEntry
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string TimestampLocal { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    public AppLogLevel Level { get; init; } = AppLogLevel.Information;
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? ExceptionDetails { get; init; }

    public override string ToString()
    {
        string levelStr = Level switch
        {
            AppLogLevel.Debug => "DEBUG",
            AppLogLevel.Information => "INFO ",
            AppLogLevel.Warning => "WARN ",
            AppLogLevel.Error => "ERROR",
            _ => "INFO "
        };

        var sb = new StringBuilder();
        sb.Append('[').Append(TimestampLocal).Append("] [").Append(levelStr).Append("] [").Append(Source).Append("] ").Append(Message);
        if (!string.IsNullOrEmpty(ExceptionDetails))
        {
            sb.AppendLine();
            sb.Append("    ").Append(ExceptionDetails.Replace("\n", "\n    ", StringComparison.Ordinal));
        }
        return sb.ToString();
    }
}

/// <summary>
/// Thread-safe, non-blocking asynchronous diagnostic logger for SafeSpeak subsystems.
/// Bounded disk persistence (%LOCALAPPDATA%\SafeSpeak\Logs\debug.log) and in-memory ring buffer.
/// </summary>
public sealed class AppLogger : IAsyncDisposable, IDisposable
{
    private const int DefaultRingBufferCapacity = 200;
    private const long DefaultMaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private static AppLogger s_instance = new();
    public static AppLogger Instance
    {
        get => Volatile.Read(ref s_instance);
        set => Volatile.Write(ref s_instance, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public static event Action<AppLogEntry>? GlobalEntryLogged;

    public static void LogDebug(string source, string message) =>
        Instance.Log(AppLogLevel.Debug, source, message);

    public static void LogInformation(string source, string message) =>
        Instance.Log(AppLogLevel.Information, source, message);

    public static void LogWarning(string source, string message, Exception? ex = null) =>
        Instance.Log(AppLogLevel.Warning, source, message, ex);

    public static void LogError(string source, string message, Exception? ex = null) =>
        Instance.Log(AppLogLevel.Error, source, message, ex);

    public static IReadOnlyList<AppLogEntry> GetRecentEntries() =>
        Instance.GetRecentSnapshot();

    private readonly string _logsDirectory;
    private readonly string _logFilePath;
    private readonly string _oldLogFilePath;
    private readonly long _maxFileSizeBytes;
    private readonly int _ringBufferCapacity;
    private readonly bool _writeToFile;
    private readonly Channel<AppLogEntry> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task? _workerTask;
    private readonly object _ringLock = new();
    private readonly AppLogEntry[] _ringBuffer;
    private int _ringBufferStart;
    private int _ringBufferCount;
    private bool _disposed;

    public event Action<AppLogEntry>? EntryLogged;

    public string LogsDirectory => _logsDirectory;
    public string LogFilePath => _logFilePath;

    public AppLogger(
        string? logsDirectory = null,
        long maxFileSizeBytes = DefaultMaxFileSizeBytes,
        int ringBufferCapacity = DefaultRingBufferCapacity,
        bool writeToFile = true)
    {
        _logsDirectory = logsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSpeak",
            "Logs");
        _logFilePath = Path.Combine(_logsDirectory, "debug.log");
        _oldLogFilePath = Path.Combine(_logsDirectory, "debug.old.log");
        _maxFileSizeBytes = Math.Max(256, maxFileSizeBytes);
        _ringBufferCapacity = Math.Max(2, ringBufferCapacity);
        _writeToFile = writeToFile;
        _ringBuffer = new AppLogEntry[_ringBufferCapacity];

        if (_writeToFile)
        {
            _channel = Channel.CreateBounded<AppLogEntry>(new BoundedChannelOptions(2048)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
            _workerTask = Task.Run(ProcessQueueAsync);
        }
        else
        {
            _channel = Channel.CreateBounded<AppLogEntry>(1);
        }
    }

    public void Log(AppLogLevel level, string source, string message, Exception? exception = null)
    {
        var entry = new AppLogEntry
        {
            Level = level,
            Source = source ?? "General",
            Message = message ?? string.Empty,
            ExceptionDetails = exception?.ToString()
        };

        // Update in-memory ring buffer
        lock (_ringLock)
        {
            if (_ringBufferCount < _ringBufferCapacity)
            {
                int index = (_ringBufferStart + _ringBufferCount) % _ringBufferCapacity;
                _ringBuffer[index] = entry;
                _ringBufferCount++;
            }
            else
            {
                _ringBuffer[_ringBufferStart] = entry;
                _ringBufferStart = (_ringBufferStart + 1) % _ringBufferCapacity;
            }
        }

        // Fire notifications
        try
        {
            EntryLogged?.Invoke(entry);
            GlobalEntryLogged?.Invoke(entry);
        }
        catch
        {
            // Logging callbacks must never throw into callers
        }

        // Enqueue to background file writer
        if (_writeToFile && !_disposed)
        {
            _channel.Writer.TryWrite(entry);
        }
    }

    public IReadOnlyList<AppLogEntry> GetRecentSnapshot()
    {
        lock (_ringLock)
        {
            var result = new List<AppLogEntry>(_ringBufferCount);
            for (int i = 0; i < _ringBufferCount; i++)
            {
                int index = (_ringBufferStart + i) % _ringBufferCapacity;
                result.Add(_ringBuffer[index]);
            }
            return result;
        }
    }

    private async Task ProcessQueueAsync()
    {
        StreamWriter? writer = null;
        try
        {
            Directory.CreateDirectory(_logsDirectory);
            writer = OpenWriter();

            var reader = _channel.Reader;
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var entry))
                {
                    try
                    {
                        WriteEntry(ref writer, entry);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // File I/O transient error; recreate writer if needed
                        try { writer?.Dispose(); } catch { }
                        writer = null;
                        await Task.Delay(250, _cts.Token).ConfigureAwait(false);
                        try { writer = OpenWriter(); } catch { }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch
        {
            // Suppress background worker crashes
        }
        finally
        {
            try
            {
                // Drain remaining items on shutdown
                while (_channel.Reader.TryRead(out var entry))
                {
                    WriteEntry(ref writer, entry);
                }
            }
            catch
            {
            }
            finally
            {
                if (writer is not null)
                {
                    try
                    {
                        writer.Flush();
                        writer.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    private void WriteEntry(ref StreamWriter? writer, AppLogEntry entry)
    {
        if (writer is not null && writer.BaseStream.Length >= _maxFileSizeBytes)
        {
            writer.Dispose();
            writer = null;
            RotateLogFiles();
        }

        writer ??= OpenWriter();
        writer.WriteLine(entry.ToString());
        writer.Flush();
    }

    private StreamWriter OpenWriter()
    {
        Directory.CreateDirectory(_logsDirectory);
        var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        return new StreamWriter(stream, Encoding.UTF8);
    }

    private void RotateLogFiles()
    {
        try
        {
            if (File.Exists(_oldLogFilePath))
            {
                File.Delete(_oldLogFilePath);
            }
            if (File.Exists(_logFilePath))
            {
                File.Move(_logFilePath, _oldLogFilePath);
            }
        }
        catch
        {
            // If rotation fails (e.g. file lock), continue writing to current file
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_writeToFile)
        {
            _channel.Writer.TryComplete();
            if (_workerTask is not null)
            {
                try
                {
                    await _workerTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _cts.Cancel();
                    try
                    {
                        await _workerTask.WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
                catch
                {
                }
            }
            _cts.Dispose();
        }
    }
}
