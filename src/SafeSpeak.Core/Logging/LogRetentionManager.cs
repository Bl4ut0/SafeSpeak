using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace SafeSpeak.Core.Logging;

/// <summary>
/// Enforces log retention policies across SafeSpeak diagnostic and chat audit logs.
/// Retains log files for the specified retention period (default 10 days) and
/// safely prunes older log files.
/// </summary>
public static partial class LogRetentionManager
{
    public const int DefaultRetentionDays = 10;

    [GeneratedRegex(@"^(?:StreamAudit|debug)_(\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatePatternRegex();

    /// <summary>
    /// Scans the specified logs directory and deletes any chat audit logs (<c>StreamAudit_*.log</c>)
    /// or archived debug logs (<c>debug_*.log</c>, <c>debug.old.log</c>) that are older than the retention cutoff.
    /// Active or recently updated log files are always preserved.
    /// </summary>
    /// <param name="logsDirectory">Path to the directory containing SafeSpeak log files.</param>
    /// <param name="retentionDays">Number of days of logs to retain (default 10).</param>
    /// <param name="referenceTime">Optional reference time for testing (defaults to DateTimeOffset.UtcNow).</param>
    /// <returns>The number of expired log files deleted.</returns>
    public static int CleanOldLogs(
        string logsDirectory,
        int retentionDays = DefaultRetentionDays,
        DateTimeOffset? referenceTime = null)
    {
        if (string.IsNullOrWhiteSpace(logsDirectory) || !Directory.Exists(logsDirectory))
        {
            return 0;
        }

        DateTimeOffset now = referenceTime ?? DateTimeOffset.UtcNow;
        DateTime cutoffDate = now.Date.AddDays(-Math.Max(1, retentionDays));

        int deletedCount = 0;

        try
        {
            var directoryInfo = new DirectoryInfo(logsDirectory);
            FileInfo[] files = directoryInfo.GetFiles("*.log");

            foreach (FileInfo file in files)
            {
                // Never delete the active debug.log directly by name
                if (string.Equals(file.Name, "debug.log", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isCandidate = file.Name.StartsWith("StreamAudit_", StringComparison.OrdinalIgnoreCase) ||
                                   file.Name.StartsWith("debug_", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(file.Name, "debug.old.log", StringComparison.OrdinalIgnoreCase);

                if (!isCandidate)
                {
                    continue;
                }

                if (IsFileExpired(file, cutoffDate))
                {
                    try
                    {
                        file.Delete();
                        deletedCount++;
                    }
                    catch (Exception)
                    {
                        // Safely ignore file locks or transient I/O issues during cleanup
                    }
                }
            }
        }
        catch (Exception)
        {
            // Defensive: directory enumeration or permission failure should never crash SafeSpeak
        }

        return deletedCount;
    }

    /// <summary>
    /// Determines whether a log file is older than the cutoff date, checking the filename's
    /// embedded date first, with fallback to the file's LastWriteTimeUtc.
    /// </summary>
    public static bool IsFileExpired(FileInfo file, DateTime cutoffDate)
    {
        if (TryExtractDateFromFileName(file.Name, out DateTime fileDate))
        {
            return fileDate.Date < cutoffDate.Date;
        }

        // Fallback for files without a date stamp in the name (e.g. debug.old.log)
        return file.LastWriteTimeUtc.Date < cutoffDate.Date;
    }

    /// <summary>
    /// Extracts a <c>yyyy-MM-dd</c> date prefix from standard SafeSpeak log file names.
    /// </summary>
    public static bool TryExtractDateFromFileName(string fileName, out DateTime date)
    {
        Match match = DatePatternRegex().Match(fileName);
        if (match.Success &&
            DateTime.TryParseExact(
                match.Groups[1].Value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            return true;
        }

        date = default;
        return false;
    }
}
