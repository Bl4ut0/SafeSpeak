using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using SafeSpeak.Core.Diagnostics;

namespace SafeSpeak.Core.Logging;

public sealed record DiagnosticBundleResult(
    string ArchivePath,
    int FileCount,
    bool IncludesStreamAuditLogs,
    long ArchiveBytes);

/// <summary>
/// Creates a bounded support archive from recent SafeSpeak logs. Diagnostic
/// logs are sanitized before packaging. Raw stream audit logs are included only
/// when the caller explicitly opts in.
/// </summary>
public static partial class DiagnosticBundleService
{
    public const int DefaultDays = 10;

    public static DiagnosticBundleResult Create(
        string logsDirectory,
        string destinationZipPath,
        bool includeStreamAuditLogs,
        string applicationVersion,
        DateTimeOffset? referenceTime = null,
        DiagnosticSystemInfo? systemInfo = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);

        string fullLogsDirectory = Path.GetFullPath(logsDirectory);
        string fullDestination = Path.GetFullPath(destinationZipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);

        DateTime cutoffUtc = (referenceTime ?? DateTimeOffset.UtcNow)
            .UtcDateTime.Date.AddDays(-(DefaultDays - 1));
        FileInfo[] candidates = Directory.Exists(fullLogsDirectory)
            ? new DirectoryInfo(fullLogsDirectory)
                .GetFiles("*.log", SearchOption.TopDirectoryOnly)
                .Where(file => file.LastWriteTimeUtc >= cutoffUtc)
                .Where(file => IsDiagnosticLog(file.Name) ||
                    includeStreamAuditLogs)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        string temporaryPath = fullDestination + ".tmp";
        if (candidates.Length > 512 || candidates.Any(file => file.Length > 10 * 1024 * 1024) ||
            candidates.Sum(file => file.Length) > 100L * 1024 * 1024)
            throw new InvalidDataException("Diagnostic logs exceed the support package limits (512 files, 10 MB per file, 100 MB total).");
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        try
        {
            using (ZipArchive archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                long totalBytes = 0;
                foreach (FileInfo file in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ZipArchiveEntry entry = archive.CreateEntry(
                        $"logs/{file.Name}",
                        CompressionLevel.Optimal);
                    using Stream target = entry.Open();
                    if (IsStreamAuditLog(file.Name))
                    {
                        using FileStream source = new(
                            file.FullName,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        byte[] buffer = new byte[81920];
                        long fileBytes = 0;
                        int read;
                        while ((read = source.Read(buffer)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            fileBytes += read; totalBytes += read;
                            if (fileBytes > 10 * 1024 * 1024 || totalBytes > 100L * 1024 * 1024)
                                throw new InvalidDataException("Logs grew beyond the support package size limits.");
                            target.Write(buffer, 0, read);
                        }
                    }
                    else
                    {
                        string text = ReadSharedText(file.FullName, cancellationToken);
                        byte[] sanitized = Encoding.UTF8.GetBytes(SanitizeDiagnosticText(text));
                        totalBytes += sanitized.Length;
                        if (sanitized.Length > 10 * 1024 * 1024 || totalBytes > 100L * 1024 * 1024)
                            throw new InvalidDataException("Diagnostic logs exceed the support package size limits.");
                        target.Write(sanitized);
                    }
                }

                ZipArchiveEntry manifest = archive.CreateEntry(
                    "diagnostic-manifest.txt",
                    CompressionLevel.Optimal);
                using var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false));
                writer.WriteLine("SafeSpeak diagnostic support package");
                writer.WriteLine($"Created UTC: {DateTimeOffset.UtcNow:O}");
                writer.WriteLine($"Application version: {applicationVersion}");
                writer.WriteLine($"Log window: last {DefaultDays} days");
                writer.WriteLine($"Raw stream audit logs included: {includeStreamAuditLogs}");
                writer.WriteLine($"Packaged log files: {candidates.Length}");
                foreach (FileInfo file in candidates) writer.WriteLine($"- {file.Name}");
                writer.Dispose();
                if (systemInfo is not null)
                {
                    var info = archive.CreateEntry("system-info.json", CompressionLevel.Optimal);
                    using var infoWriter = new StreamWriter(info.Open(), new UTF8Encoding(false));
                    infoWriter.Write(JsonSerializer.Serialize(systemInfo, new JsonSerializerOptions { WriteIndented = true }));
                }
            }

            File.Move(temporaryPath, fullDestination, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporaryPath); } catch { }
            throw;
        }

        return new DiagnosticBundleResult(
            fullDestination,
            candidates.Length,
            includeStreamAuditLogs,
            new FileInfo(fullDestination).Length);
    }

    private static bool IsDiagnosticLog(string name) =>
        name.Equals("debug.log", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("debug.old.log", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("startup_error.log", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("debug_", StringComparison.OrdinalIgnoreCase);

    private static bool IsStreamAuditLog(string name) =>
        name.StartsWith("StreamAudit_", StringComparison.OrdinalIgnoreCase);

    private static string ReadSharedText(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var text = new StringBuilder();
        char[] buffer = new char[4096];
        int read;
        while ((read = reader.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (text.Length + read > 10 * 1024 * 1024)
                throw new InvalidDataException("Diagnostic log exceeds the support package size limit.");
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }

    internal static string SanitizeDiagnosticText(string text)
    {
        string sanitized = DiscordWebhookRegex().Replace(text, "[REDACTED DISCORD WEBHOOK]");
        sanitized = AuthorizationRegex().Replace(sanitized, "$1[REDACTED]");
        sanitized = MachineNameRegex().Replace(sanitized, "Machine=[REDACTED]");
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            sanitized = sanitized.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        return sanitized;
    }

    [GeneratedRegex(@"https://(?:canary\.|ptb\.)?discord(?:app)?\.com/api/webhooks/[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex DiscordWebhookRegex();

    [GeneratedRegex(@"(?i)(Authorization\s*[:=]\s*(?:Bearer|Bot)\s+)[^\s,;]+")]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex(@"Machine=[^,\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex MachineNameRegex();
}

public sealed record DiagnosticSystemInfo
{
    public int PayloadFormatVersion { get; init; } = 1;
    public required string HostId { get; init; }
    public required string ApplicationVersion { get; init; }
    public string OperatingSystem { get; init; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    public string Framework { get; init; } = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    public string Architecture { get; init; } = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
    public int LogicalProcessors { get; init; } = Environment.ProcessorCount;
    public string ModerationModel { get; init; } = "Unknown";
    public string Voice { get; init; } = "Default";
    public int SpeechThreads { get; init; }
    public PerformanceSnapshot? Performance { get; init; }
}
