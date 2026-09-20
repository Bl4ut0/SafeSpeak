using System.IO.Compression;
using SafeSpeak.Core.Logging;

namespace SafeSpeak.Core.Tests;

public sealed class DiagnosticBundleServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "SafeSpeakDiagnosticBundleTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_DefaultPackageSanitizesDiagnosticsAndExcludesRawAuditLogs()
    {
        string logs = Path.Combine(_root, "logs");
        Directory.CreateDirectory(logs);
        string debug = Path.Combine(logs, "debug.log");
        File.WriteAllText(debug,
            $"Machine=STREAM-PC, path={Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\file " +
            "https://discord.com/api/webhooks/123/secret");
        File.WriteAllText(Path.Combine(logs, "StreamAudit_2026-09-14.log"), "raw username and chat");
        string old = Path.Combine(logs, "debug_2020-01-01.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-20));

        string zip = Path.Combine(_root, "support.zip");
        DiagnosticBundleResult result = DiagnosticBundleService.Create(
            logs, zip, includeStreamAuditLogs: false, "1.2.3");

        Assert.Equal(1, result.FileCount);
        Assert.False(result.IncludesStreamAuditLogs);
        using ZipArchive archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, entry => entry.FullName == "logs/debug.log");
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("StreamAudit"));
        using var reader = new StreamReader(archive.GetEntry("logs/debug.log")!.Open());
        string packaged = reader.ReadToEnd();
        Assert.Contains("Machine=[REDACTED]", packaged);
        Assert.Contains("[REDACTED DISCORD WEBHOOK]", packaged);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), packaged);
    }

    [Fact]
    public void Create_IncludesRawAuditLogOnlyAfterExplicitOptIn()
    {
        string logs = Path.Combine(_root, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "StreamAudit_2026-09-14.log"), "raw username and chat");

        File.WriteAllText(Path.Combine(logs, "connector-history.log"), "connector detail");
        string zip = Path.Combine(_root, "support-with-chat.zip");
        DiagnosticBundleResult result = DiagnosticBundleService.Create(
            logs, zip, includeStreamAuditLogs: true, "1.2.3");

        Assert.True(result.IncludesStreamAuditLogs);
        Assert.Equal(2, result.FileCount);
        using ZipArchive archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, entry => entry.FullName.Contains("StreamAudit"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void PackageIncludesHostAndSystemInformation()
    {
        string host = Guid.NewGuid().ToString();
        string zip = Path.Combine(_root, "system.zip");
        DiagnosticBundleService.Create(Path.Combine(_root, "logs"), zip, false, "1.2.3",
            systemInfo: new DiagnosticSystemInfo { HostId = host, ApplicationVersion = "1.2.3" });
        using var archive = ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry("system-info.json")!.Open());
        string info = reader.ReadToEnd();
        Assert.Contains(host, info); Assert.Contains("LogicalProcessors", info);
        Assert.DoesNotContain(Environment.MachineName, info);
    }
}
