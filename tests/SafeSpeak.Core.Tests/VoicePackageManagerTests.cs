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

    private string CreatePackageArchive(VoicePackageManifest manifest)
    {
        Directory.CreateDirectory(_root);
        string archivePath = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        WriteEntry(archive, "voice.json", JsonSerializer.Serialize(manifest));
        WriteEntry(archive, "model.onnx", "model data");
        WriteEntry(archive, "model.onnx.json", "{}");
        return archivePath;
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
