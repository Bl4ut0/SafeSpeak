using System.Globalization;
using System.Text;
using System.Threading.Channels;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Logging;

public sealed record StreamAuditRecord
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string TimestampLocal { get; init; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    public string EventType { get; init; } = "Chat";
    public string Author { get; init; } = string.Empty;
    public string AuthorDisplayName { get; init; } = string.Empty;
    public string AuthorTier { get; init; } = string.Empty;
    public bool IsSubscriber { get; init; }
    public bool IsModerator { get; init; }
    public string RawUnfilteredText { get; init; } = string.Empty;
    public string NormalizedText { get; init; } = string.Empty;
    public string Disposition { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public string ReasonDescription { get; init; } = string.Empty;
    public double ToxicityScore { get; init; }
    public IReadOnlyList<string> TriggeredRules { get; init; } = Array.Empty<string>();
    public string SpokenText { get; init; } = string.Empty;
    public string? GiftName { get; init; }
    public int? GiftCount { get; init; }
}

/// <summary>
/// Writes explicitly enabled, session-stamped audit logs without performing
/// file I/O on the moderation path. Record work is bounded; lifecycle commands
/// remain ordered so reconnects cannot route an old session into a new file.
/// </summary>
public sealed class StreamAuditLogger : IAsyncDisposable
{
    private const int DefaultQueueCapacity = 1024;
    private static int s_fileSequence;

    private readonly string _logsDirectory;
    private readonly int _queueCapacity;
    private readonly Channel<LogWorkItem> _workQueue;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Task _writeWorker;
    private readonly object _stateLock = new();

    private SessionContext? _activeSession;
    private SessionContext? _lastSession;
    private string? _lastError;
    private int _queuedRecordCount;
    private long _totalDroppedRecords;
    private bool _isEnabled;
    private bool _disposed;

    public StreamAuditLogger(string? customLogsDirectory = null, int queueCapacity = DefaultQueueCapacity)
    {
        if (queueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueCapacity));
        }

        _logsDirectory = customLogsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSpeak",
            "Logs");
        _queueCapacity = queueCapacity;
        _workQueue = Channel.CreateUnbounded<LogWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _writeWorker = Task.Run(ProcessLogQueueAsync);
    }

    public bool IsEnabled
    {
        get
        {
            lock (_stateLock) return _isEnabled;
        }
        set
        {
            lock (_stateLock)
            {
                if (_disposed || _isEnabled == value) return;
                _isEnabled = value;
                if (!value) QueueEndActiveSessionLocked();
            }
        }
    }

    public string LogsDirectory => _logsDirectory;
    public string? CurrentLogFilePath
    {
        get
        {
            lock (_stateLock) return _activeSession?.FilePath;
        }
    }
    public string? LastLogFilePath
    {
        get
        {
            lock (_stateLock) return _lastSession?.FilePath;
        }
    }
    public string? LastError
    {
        get
        {
            lock (_stateLock) return _lastError;
        }
    }
    public bool HasActiveSession
    {
        get
        {
            lock (_stateLock) return _activeSession is not null;
        }
    }

    public int TotalMessages => Volatile.Read(ref GetCounterSession().TotalMessages);
    public int TotalApproved => Volatile.Read(ref GetCounterSession().TotalApproved);
    public int TotalRejected => Volatile.Read(ref GetCounterSession().TotalRejected);
    public long DroppedRecordCount => Interlocked.Read(ref _totalDroppedRecords);

    public bool StartSession(string sourceName, string endpointUrl, ModerationConfig? config = null)
    {
        lock (_stateLock)
        {
            if (_disposed || !_isEnabled) return false;
            QueueEndActiveSessionLocked();

            var session = new SessionContext(
                CreateUniqueLogPath(),
                DateTimeOffset.UtcNow,
                BuildHeader(sourceName, endpointUrl, config));
            _activeSession = session;
            _lastSession = session;
            _lastError = null;

            if (_workQueue.Writer.TryWrite(LogWorkItem.Start(session))) return true;
            _activeSession = null;
            _lastError = "The audit log writer is unavailable.";
            return false;
        }
    }

    public void LogDecision(ChatMessage rawMessage, ModerationDecision decision)
    {
        if (rawMessage is null || decision is null) return;

        var record = new StreamAuditRecord
        {
            TimestampUtc = rawMessage.TimestampUtc,
            TimestampLocal = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            EventType = "Chat",
            Author = rawMessage.Author,
            AuthorDisplayName = rawMessage.AuthorDisplayName ?? rawMessage.Author,
            AuthorTier = rawMessage.AuthorTier.ToString(),
            IsSubscriber = rawMessage.IsSubscriber,
            IsModerator = rawMessage.IsModerator,
            RawUnfilteredText = rawMessage.RawText,
            NormalizedText = decision.NormalizedText,
            Disposition = decision.Disposition.ToString().ToUpperInvariant(),
            ReasonCode = decision.ReasonCode.ToString(),
            ReasonDescription = decision.ReasonDescription,
            ToxicityScore = decision.ToxicityScore,
            TriggeredRules = decision.TriggeredRules,
            SpokenText = decision.SpokenText
        };

        TryQueueRecord(FormatRecord(record), session =>
        {
            Interlocked.Increment(ref session.TotalMessages);
            if (decision.Passed)
            {
                Interlocked.Increment(ref session.TotalApproved);
            }
            else
            {
                Interlocked.Increment(ref session.TotalRejected);
            }
        });
    }

    public void LogEvent(LivestreamEvent liveEvent, ModerationDecision? decision)
    {
        if (liveEvent is null) return;

        var record = new StreamAuditRecord
        {
            TimestampUtc = liveEvent.TimestampUtc,
            TimestampLocal = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            EventType = liveEvent.Type.ToString(),
            Author = liveEvent.Author,
            AuthorDisplayName = liveEvent.AuthorDisplayName ?? liveEvent.Author,
            AuthorTier = liveEvent.AuthorTier.ToString(),
            IsSubscriber = liveEvent.IsSubscriber,
            IsModerator = liveEvent.IsModerator,
            RawUnfilteredText = liveEvent.Text ?? string.Empty,
            NormalizedText = decision?.NormalizedText ?? string.Empty,
            Disposition = decision is not null ? decision.Disposition.ToString().ToUpperInvariant() : "EVENT",
            ReasonCode = decision?.ReasonCode.ToString() ?? "None",
            ReasonDescription = decision?.ReasonDescription ?? "Live Event Processed",
            ToxicityScore = decision?.ToxicityScore ?? 0.0,
            TriggeredRules = decision?.TriggeredRules ?? Array.Empty<string>(),
            SpokenText = decision?.SpokenText ?? string.Empty,
            GiftName = liveEvent.GiftName,
            GiftCount = liveEvent.GiftCount
        };

        TryQueueRecord(FormatRecord(record), session =>
        {
            if (liveEvent.Type == LivestreamEventType.Gift)
            {
                Interlocked.Increment(ref session.TotalGifts);
            }
        });
    }

    public void EndSession()
    {
        lock (_stateLock) QueueEndActiveSessionLocked();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool> completion;
        lock (_stateLock)
        {
            if (_disposed) return;
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_workQueue.Writer.TryWrite(LogWorkItem.Flush(completion))) return;
        }

        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    private void TryQueueRecord(string entry, Action<SessionContext> updateCounters)
    {
        lock (_stateLock)
        {
            if (_disposed || !_isEnabled || _activeSession is null) return;

            SessionContext session = _activeSession;
            if (!TryReserveRecordSlot())
            {
                Interlocked.Increment(ref session.DroppedRecords);
                Interlocked.Increment(ref _totalDroppedRecords);
                return;
            }

            if (!_workQueue.Writer.TryWrite(LogWorkItem.Record(session, entry)))
            {
                Interlocked.Decrement(ref _queuedRecordCount);
                Interlocked.Increment(ref session.DroppedRecords);
                Interlocked.Increment(ref _totalDroppedRecords);
                return;
            }

            updateCounters(session);
        }
    }

    private bool TryReserveRecordSlot()
    {
        while (true)
        {
            int queued = Volatile.Read(ref _queuedRecordCount);
            if (queued >= _queueCapacity) return false;
            if (Interlocked.CompareExchange(ref _queuedRecordCount, queued + 1, queued) == queued)
            {
                return true;
            }
        }
    }

    private void QueueEndActiveSessionLocked()
    {
        SessionContext? session = _activeSession;
        if (session is null) return;

        _activeSession = null;
        _lastSession = session;
        _workQueue.Writer.TryWrite(LogWorkItem.End(session));
    }

    private SessionContext GetCounterSession()
    {
        lock (_stateLock)
        {
            return _activeSession ?? _lastSession ?? SessionContext.Empty;
        }
    }

    private string CreateUniqueLogPath()
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture);
        int sequence = Interlocked.Increment(ref s_fileSequence);
        return Path.Combine(_logsDirectory, $"StreamAudit_{timestamp}_{sequence:D4}.log");
    }
    private static string BuildHeader(string sourceName, string endpointUrl, ModerationConfig? config)
    {
        var header = new StringBuilder();
        header.AppendLine("================================================================================");
        header.AppendLine("                       SAFESPEAK STREAM AUDIT LOG");
        header.AppendLine("================================================================================");
        header.AppendLine($"Stream Session Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local)");
        header.AppendLine($"Live Source:            {EscapeForLog(sourceName)} ({EscapeForLog(endpointUrl)})");
        if (config is not null)
        {
            header.AppendLine($"Audience Filter:        {config.AudienceMode}");
            header.AppendLine($"Moderation Strictness:  {config.Strictness} (Level {config.IntentModerationLevel})");
            header.AppendLine($"English Only:           {config.EnglishOnly}");
            header.AppendLine($"Reject Mixed Scripts:   {config.RejectMixedScripts}");
            header.AppendLine($"AI Intent Classifier:   {config.AiClassificationEnabled} (Threshold: {config.AiToxicityThreshold:0.00})");
        }
        header.AppendLine("================================================================================");
        header.AppendLine("NOTE: All raw messages are logged unfiltered below for moderation review.");
        header.AppendLine("================================================================================");
        header.AppendLine();
        return header.ToString();
    }

    private static string BuildFooter(SessionContext session)
    {
        TimeSpan duration = DateTimeOffset.UtcNow - session.StartTime;
        var footer = new StringBuilder();
        footer.AppendLine();
        footer.AppendLine("================================================================================");
        footer.AppendLine($"Stream Session Ended:   {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local)");
        footer.AppendLine($"Total Duration:         {duration:hh\\:mm\\:ss}");
        footer.AppendLine($"Total Messages:         {Volatile.Read(ref session.TotalMessages)}");
        footer.AppendLine($"  - Approved:           {Volatile.Read(ref session.TotalApproved)}");
        footer.AppendLine($"  - Rejected:           {Volatile.Read(ref session.TotalRejected)}");
        footer.AppendLine($"Total Gifts Logged:     {Volatile.Read(ref session.TotalGifts)}");
        footer.AppendLine($"Dropped Records:        {Interlocked.Read(ref session.DroppedRecords)}");
        footer.AppendLine("================================================================================");
        footer.AppendLine();
        return footer.ToString();
    }

    private static string FormatRecord(StreamAuditRecord record)
    {
        var entry = new StringBuilder();
        string badge = record.IsModerator ? "[MOD]" : (record.IsSubscriber ? "[SUB]" : "[VIEWER]");
        entry.AppendLine(
            $"[{record.TimestampLocal}] [{record.EventType.ToUpperInvariant()}] {badge} " +
            $"@{EscapeForLog(record.Author)} (\"{EscapeForLog(record.AuthorDisplayName)}\") " +
            $"[Tier: {EscapeForLog(record.AuthorTier)}]");
        entry.AppendLine($"  RAW UNFILTERED: \"{EscapeForLog(record.RawUnfilteredText)}\"");
        if (record.EventType == "Gift" && !string.IsNullOrEmpty(record.GiftName))
        {
            entry.AppendLine($"  GIFT DETAILS:   {record.GiftCount}x {EscapeForLog(record.GiftName)}");
        }
        if (!string.IsNullOrEmpty(record.NormalizedText) && record.NormalizedText != record.RawUnfilteredText)
        {
            entry.AppendLine($"  NORMALIZED:     \"{EscapeForLog(record.NormalizedText)}\"");
        }
        entry.AppendLine(
            $"  DISPOSITION:    {record.Disposition} " +
            $"({record.ReasonCode}: {EscapeForLog(record.ReasonDescription)})");
        if (record.ToxicityScore > 0)
        {
            entry.AppendLine($"  TOXICITY SCORE: {record.ToxicityScore:0.000}");
        }
        if (record.TriggeredRules.Count > 0)
        {
            entry.AppendLine($"  TRIGGERED:      {string.Join(", ", record.TriggeredRules.Select(EscapeForLog))}");
        }
        entry.AppendLine(
            $"  SPOKEN OUTPUT:  {(string.IsNullOrWhiteSpace(record.SpokenText) ? "<SILENT/BLOCKED>" : $"\"{EscapeForLog(record.SpokenText)}\"")}");
        entry.AppendLine("--------------------------------------------------------------------------------");
        return entry.ToString();
    }

    private static string EscapeForLog(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
    private async Task ProcessLogQueueAsync()
    {
        try
        {
            await foreach (LogWorkItem work in _workQueue.Reader.ReadAllAsync(_lifetimeCts.Token).ConfigureAwait(false))
            {
                switch (work.Kind)
                {
                    case LogWorkKind.Start:
                        await OpenSessionAsync(work.Session!, _lifetimeCts.Token).ConfigureAwait(false);
                        break;
                    case LogWorkKind.Record:
                        try
                        {
                            await WriteRecordAsync(work.Session!, work.Entry!, _lifetimeCts.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref _queuedRecordCount);
                        }
                        break;
                    case LogWorkKind.End:
                        await CloseSessionAsync(work.Session!, _lifetimeCts.Token).ConfigureAwait(false);
                        break;
                    case LogWorkKind.Flush:
                        await FlushOpenSessionAsync(_lifetimeCts.Token).ConfigureAwait(false);
                        work.Completion!.TrySetResult(true);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            SessionContext? session;
            lock (_stateLock) session = _activeSession ?? _lastSession;
            SafeDisposeWriter(session);
        }
    }

    private async Task OpenSessionAsync(SessionContext session, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_logsDirectory);
            var stream = new FileStream(
                session.FilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
            session.Writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await session.Writer.WriteAsync(session.Header.AsMemory(), cancellationToken).ConfigureAwait(false);
            await session.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SafeDisposeWriter(session);
            RecordFailure(session, ex);
        }
    }

    private async Task WriteRecordAsync(SessionContext session, string entry, CancellationToken cancellationToken)
    {
        StreamWriter? writer = session.Writer;
        if (writer is null) return;

        try
        {
            await writer.WriteAsync(entry.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SafeDisposeWriter(session);
            RecordFailure(session, ex);
        }
    }

    private async Task CloseSessionAsync(SessionContext session, CancellationToken cancellationToken)
    {
        StreamWriter? writer = session.Writer;
        if (writer is null) return;

        try
        {
            await writer.WriteAsync(BuildFooter(session).AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordFailure(session, ex);
        }
        finally
        {
            SafeDisposeWriter(session);
        }
    }

    private async Task FlushOpenSessionAsync(CancellationToken cancellationToken)
    {
        SessionContext? session;
        lock (_stateLock) session = _activeSession;

        StreamWriter? writer = session?.Writer;
        if (writer is null) return;

        try
        {
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SafeDisposeWriter(session!);
            RecordFailure(session!, ex);
        }
    }

    private void RecordFailure(SessionContext session, Exception exception)
    {
        lock (_stateLock)
        {
            _lastError = $"Audit logging failed: {exception.Message}";
            if (ReferenceEquals(_activeSession, session)) _activeSession = null;
        }
    }

    private static void SafeDisposeWriter(SessionContext? session)
    {
        StreamWriter? writer = session?.Writer;
        if (writer is null) return;

        session!.Writer = null;
        try
        {
            writer.Dispose();
        }
        catch
        {
        }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            _isEnabled = false;
            QueueEndActiveSessionLocked();
            _workQueue.Writer.TryComplete();
        }

        bool workerCompleted = false;
        try
        {
            await _writeWorker.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            workerCompleted = true;
        }
        catch (TimeoutException)
        {
            _lifetimeCts.Cancel();
            try
            {
                await _writeWorker.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
                workerCompleted = true;
            }
            catch
            {
            }
        }
        catch
        {
            workerCompleted = _writeWorker.IsCompleted;
        }

        if (workerCompleted) _lifetimeCts.Dispose();
    }

    private enum LogWorkKind
    {
        Start,
        Record,
        End,
        Flush
    }

    private sealed record LogWorkItem(
        LogWorkKind Kind,
        SessionContext? Session = null,
        string? Entry = null,
        TaskCompletionSource<bool>? Completion = null)
    {
        public static LogWorkItem Start(SessionContext session) => new(LogWorkKind.Start, Session: session);
        public static LogWorkItem Record(SessionContext session, string entry) => new(LogWorkKind.Record, session, entry);
        public static LogWorkItem End(SessionContext session) => new(LogWorkKind.End, Session: session);
        public static LogWorkItem Flush(TaskCompletionSource<bool> completion) => new(LogWorkKind.Flush, Completion: completion);
    }

    private sealed class SessionContext
    {
        public static readonly SessionContext Empty = new(string.Empty, DateTimeOffset.MinValue, string.Empty);

        public SessionContext(string filePath, DateTimeOffset startTime, string header)
        {
            FilePath = filePath;
            StartTime = startTime;
            Header = header;
        }

        public string FilePath { get; }
        public DateTimeOffset StartTime { get; }
        public string Header { get; }
        public StreamWriter? Writer { get; set; }
        public int TotalMessages;
        public int TotalApproved;
        public int TotalRejected;
        public int TotalGifts;
        public long DroppedRecords;
    }
}
