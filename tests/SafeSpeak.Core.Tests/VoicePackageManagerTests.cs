using System.IO.Compression;
using System.Text.Json;
using SafeSpeak.Core.Audio.VoiceFramework;

namespace SafeSpeak.Core.Tests;

public sealed class VoicePackageManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SafeSpeakVoiceTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ImportPackage_RejectsArchivePathTraversal()
    {
        Directory.CreateDirectory(_root);
        string archivePath = Path.Combine(_root, "unsafe.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("../outside.txt");
            await using StreamWriter writer = new(entry.Open());
            await writer.WriteAsync("unsafe");
        }

        var manager = new VoicePackageManager(Path.Combine(_root, "installed"));
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.ImportPackageFromZipAsync(archivePath));
        Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
    }

    [Fact]
    public async Task ImportPackage_RejectsManifestFileTraversal()
    {
        string archivePath = CreatePackageArchive(new VoicePackageManifest
        {
            Id = "unsafe-package",
            ModelFileName = "../model.onnx",
            ConfigFileName = "model.onnx.json"
        });

        var manager = new VoicePackageManager(Path.Combine(_root, "installed"));
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.ImportPackageFromZipAsync(archivePath));
    }

    [Fact]
    public async Task ImportPackage_InstallsValidatedPackage()
    {
        string archivePath = CreatePackageArchive(new VoicePackageManifest
        {
            Id = "safe-package",
            DisplayName = "Safe package",
            ModelFileName = "model.onnx",
            ConfigFileName = "model.onnx.json"
        });

        var manager = new VoicePackageManager(Path.Combine(_root, "installed"));
        VoicePackageInfo imported = await manager.ImportPackageFromZipAsync(archivePath);

        Assert.Equal("safe-package", imported.Manifest.Id);
        Assert.True(File.Exists(imported.ModelAbsolutePath));
        Assert.True(File.Exists(imported.ConfigAbsolutePath));
    }

    [Fact]
    public async Task ImportPackage_RejectsArchiveEntryBombAndCleansStaging()
    {
        Directory.CreateDirectory(_root);
        string archivePath = Path.Combine(_root, "too-many-entries.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            for (int index = 0; index < 65; index++)
            {
                WriteEntry(archive, $"entry-{index}.txt", "data");
            }
        }

        string installedRoot = Path.Combine(_root, "installed");
        var manager = new VoicePackageManager(installedRoot);

        await Assert.ThrowsAsync<InvalidDataException>(() => manager.ImportPackageFromZipAsync(archivePath));

        AssertNoTransactionDirectories(installedRoot);
    }

    [Fact]
    public async Task ImportPackage_InvalidReplacementPreservesInstalledPackageAndCleansStaging()
    {
        string installedRoot = Path.Combine(_root, "installed");
        InstallExistingPackage(installedRoot, "replace-me", "original model");
        string archivePath = CreatePackageArchive(new VoicePackageManifest
        {
            Id = "replace-me",
            DisplayName = "Invalid replacement",
            ModelFileName = "model.onnx",
            ConfigFileName = "missing-config.json"
        }, modelContent: "replacement model");

        var manager = new VoicePackageManager(installedRoot);

        await Assert.ThrowsAsync<InvalidDataException>(() => manager.ImportPackageFromZipAsync(archivePath));

        Assert.Equal(
            "original model",
            File.ReadAllText(Path.Combine(installedRoot, "replace-me", "model.onnx")));
        AssertNoTransactionDirectories(installedRoot);
    }

    [Fact]
    public async Task ImportPackage_CommitFailureRollsBackExistingPackageAndCleansTransactions()
    {
        string installedRoot = Path.Combine(_root, "installed");
        InstallExistingPackage(installedRoot, "replace-me", "original model");
        string archivePath = CreatePackageArchive(new VoicePackageManifest
        {
            Id = "replace-me",
            DisplayName = "Replacement",
            ModelFileName = "model.onnx",
            ConfigFileName = "model.onnx.json"
        }, modelContent: "replacement model");

        var manager = new VoicePackageManager(
            installedRoot,
            afterRollbackDirectoryCreated: () => throw new IOException("Simulated commit failure."));

        await Assert.ThrowsAsync<IOException>(() => manager.ImportPackageFromZipAsync(archivePath));

        Assert.Equal(
            "original model",
            File.ReadAllText(Path.Combine(installedRoot, "replace-me", "model.onnx")));
        AssertNoTransactionDirectories(installedRoot);
    }

    [Fact]
    public async Task ImportPackage_CancellationDuringCommitRollsBackExistingPackage()
    {
        string installedRoot = Path.Combine(_root, "installed");
        InstallExistingPackage(installedRoot, "replace-me", "original model");
        string archivePath = CreatePackageArchive(new VoicePackageManifest
        {
            Id = "replace-me",
            DisplayName = "Replacement",
            ModelFileName = "model.onnx",
            ConfigFileName = "model.onnx.json"
        }, modelContent: "replacement model");
        using var cancellation = new CancellationTokenSource();
        var manager = new VoicePackageManager(
            installedRoot,
            afterRollbackDirectoryCreated: cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.ImportPackageFromZipAsync(archivePath, cancellation.Token));

        Assert.Equal(
            "original model",
            File.ReadAllText(Path.Combine(installedRoot, "replace-me", "model.onnx")));
        AssertNoTransactionDirectories(installedRoot);
    }

    [Fact]
    public async Task ImportPackage_ValidatedReplacementCommitsAndRemovesRollbackDirectory()
    {
        string installedRoot = Path.Combine(_root, "installed");
        InstallExistingPackage(installedRoot, "replace-me", "original model");
        string archivePath = CreatePackageArchive(new VoicePackageManifest
        {
            Id = "replace-me",
            DisplayName = "Replacement",
            ModelFileName = "model.onnx",
            ConfigFileName = "model.onnx.json"
        }, modelContent: "replacement model");
        var manager = new VoicePackageManager(installedRoot);

        VoicePackageInfo imported = await manager.ImportPackageFromZipAsync(archivePath);

        Assert.Equal("replacement model", File.ReadAllText(imported.ModelAbsolutePath));
        AssertNoTransactionDirectories(installedRoot);
    }

    [Fact]
    public void GetInstalledPackages_DoesNotExposeTransactionDirectories()
    {
        string installedRoot = Path.Combine(_root, "installed");
        InstallExistingPackage(installedRoot, "installed-pack", "installed model");
        InstallExistingPackage(installedRoot, ".import-in-progress", "staged model");
        InstallExistingPackage(installedRoot, ".rollback-old-pack", "rollback model");
        var manager = new VoicePackageManager(installedRoot);

        VoicePackageInfo installed = Assert.Single(manager.GetInstalledPackages());

        Assert.Equal("installed-pack", installed.Manifest.Id);
    }

    private string CreatePackageArchive(VoicePackageManifest manifest, string modelContent = "model data")
    {
        Directory.CreateDirectory(_root);
        string archivePath = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        WriteEntry(archive, "voice.json", JsonSerializer.Serialize(manifest));
        WriteEntry(archive, "model.onnx", modelContent);
        WriteEntry(archive, "model.onnx.json", "{}");
        return archivePath;
    }

    private static void InstallExistingPackage(string installedRoot, string packageId, string modelContent)
    {
        string packageDirectory = Path.Combine(installedRoot, packageId);
        Directory.CreateDirectory(packageDirectory);
        var manifest = new VoicePackageManifest
        {
            Id = packageId,
            DisplayName = "Existing package",
            ModelFileName = "model.onnx",
            ConfigFileName = "model.onnx.json"
        };

        File.WriteAllText(Path.Combine(packageDirectory, "voice.json"), JsonSerializer.Serialize(manifest));
        File.WriteAllText(Path.Combine(packageDirectory, "model.onnx"), modelContent);
        File.WriteAllText(Path.Combine(packageDirectory, "model.onnx.json"), "{}");
    }

    private static void AssertNoTransactionDirectories(string installedRoot)
    {
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(installedRoot),
            path =>
            {
                string name = Path.GetFileName(path);
                return name.StartsWith(".import-", StringComparison.Ordinal) ||
                       name.StartsWith(".rollback-", StringComparison.Ordinal);
            });
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using StreamWriter writer = new(entry.Open());
        writer.Write(content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
