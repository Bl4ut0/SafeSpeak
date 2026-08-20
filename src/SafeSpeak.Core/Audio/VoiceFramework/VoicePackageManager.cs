using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SafeSpeak.Core.Audio.VoiceFramework;

public sealed class VoicePackageInfo
{
    public required VoicePackageManifest Manifest { get; init; }
    public required string PackageDirectory { get; init; }
    public required string ModelAbsolutePath { get; init; }
    public required string ConfigAbsolutePath { get; init; }
    public string? SampleAudioAbsolutePath { get; init; }
}

public sealed class VoicePackageManager
{
    private const int MaximumArchiveEntries = 64;
    private const long MaximumExpandedBytes = 512L * 1024 * 1024;
    private static readonly Regex SafeIdPattern = new("^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    private readonly string _voicePacksRoot;

    public string VoicePacksRoot => _voicePacksRoot;

    public VoicePackageManager(string? customRoot = null)
    {
        _voicePacksRoot = customRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSpeak",
            "Voices",
            "Packs"
        );

        if (!Directory.Exists(_voicePacksRoot))
        {
            Directory.CreateDirectory(_voicePacksRoot);
        }
    }

    public IReadOnlyList<VoicePackageInfo> GetInstalledPackages()
    {
        var list = new List<VoicePackageInfo>();
        if (!Directory.Exists(_voicePacksRoot)) return list;

        foreach (var dir in Directory.GetDirectories(_voicePacksRoot))
        {
            string manifestPath = Path.Combine(dir, "voice.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                string json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<VoicePackageManifest>(json);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id)) continue;

                string modelPath = Path.Combine(dir, manifest.ModelFileName);
                string configPath = Path.Combine(dir, manifest.ConfigFileName);
                string samplePath = Path.Combine(dir, manifest.SampleAudioFileName ?? "sample.wav");

                if (File.Exists(modelPath))
                {
                    list.Add(new VoicePackageInfo
                    {
                        Manifest = manifest,
                        PackageDirectory = dir,
                        ModelAbsolutePath = modelPath,
                        ConfigAbsolutePath = configPath,
                        SampleAudioAbsolutePath = File.Exists(samplePath) ? samplePath : null
                    });
                }
            }
            catch { }
        }

        return list;
    }

    public async Task<VoicePackageInfo> ImportPackageFromZipAsync(string zipOrVoicePackPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipOrVoicePackPath))
        {
            throw new FileNotFoundException($"Voice pack archive not found: {zipOrVoicePackPath}");
        }

        string tempExtract = Path.Combine(Path.GetTempPath(), "SafeSpeak_VoiceImport_" + Guid.NewGuid().ToString("N"));
        try
        {
            ExtractArchiveSafely(zipOrVoicePackPath, tempExtract);

            string manifestPath = Path.Combine(tempExtract, "voice.json");
            if (!File.Exists(manifestPath))
            {
                // Check if enclosed in a subfolder
                var subDirs = Directory.GetDirectories(tempExtract);
                if (subDirs.Length == 1 && File.Exists(Path.Combine(subDirs[0], "voice.json")))
                {
                    manifestPath = Path.Combine(subDirs[0], "voice.json");
                }
                else
                {
                    throw new InvalidDataException("Invalid voice pack archive: 'voice.json' manifest is missing.");
                }
            }

            string json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<VoicePackageManifest>(json)
                ?? throw new InvalidDataException("Failed to deserialize voice pack manifest.");

            if (string.IsNullOrWhiteSpace(manifest.Id))
            {
                manifest.Id = Path.GetFileNameWithoutExtension(zipOrVoicePackPath).ToLowerInvariant().Replace(" ", "-");
            }

            ValidateManifest(manifest);

            string sourceDir = Path.GetDirectoryName(manifestPath)!;
            string sourceModelPath = ResolvePackageFile(sourceDir, manifest.ModelFileName, "model");
            string sourceConfigPath = ResolvePackageFile(sourceDir, manifest.ConfigFileName, "configuration");
            if (!File.Exists(sourceModelPath) || !File.Exists(sourceConfigPath))
            {
                throw new InvalidDataException("The voice package is missing its model or configuration file.");
            }

            string targetDir = Path.Combine(_voicePacksRoot, manifest.Id);
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }

            Directory.Move(sourceDir, targetDir);

            string finalModelPath = Path.Combine(targetDir, manifest.ModelFileName);
            string finalConfigPath = Path.Combine(targetDir, manifest.ConfigFileName);
            string finalSamplePath = Path.Combine(targetDir, manifest.SampleAudioFileName ?? "sample.wav");

            return new VoicePackageInfo
            {
                Manifest = manifest,
                PackageDirectory = targetDir,
                ModelAbsolutePath = finalModelPath,
                ConfigAbsolutePath = finalConfigPath,
                SampleAudioAbsolutePath = File.Exists(finalSamplePath) ? finalSamplePath : null
            };
        }
        finally
        {
            if (Directory.Exists(tempExtract))
            {
                try { Directory.Delete(tempExtract, true); } catch { }
            }
        }
    }

    private static void ExtractArchiveSafely(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        string destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        long expandedBytes = 0;
        int entryCount = 0;

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (++entryCount > MaximumArchiveEntries)
            {
                throw new InvalidDataException($"Voice packages may contain at most {MaximumArchiveEntries} files.");
            }

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumExpandedBytes)
            {
                throw new InvalidDataException("The expanded voice package is larger than 512 MB.");
            }

            string destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The voice package contains an unsafe file path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void ValidateManifest(VoicePackageManifest manifest)
    {
        if (!SafeIdPattern.IsMatch(manifest.Id))
        {
            throw new InvalidDataException("The voice package ID contains unsupported characters.");
        }

        _ = ResolvePackageFile("C:\\SafeSpeakPackageRoot", manifest.ModelFileName, "model");
        _ = ResolvePackageFile("C:\\SafeSpeakPackageRoot", manifest.ConfigFileName, "configuration");
        if (!string.IsNullOrWhiteSpace(manifest.SampleAudioFileName))
        {
            _ = ResolvePackageFile("C:\\SafeSpeakPackageRoot", manifest.SampleAudioFileName, "sample audio");
        }
    }

    private static string ResolvePackageFile(string packageDirectory, string fileName, string description)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException($"The voice package {description} filename is unsafe.");
        }

        return Path.Combine(packageDirectory, fileName);
    }

    public async Task<string> ExportPackageToZipAsync(string voiceId, string destinationZipPath, CancellationToken cancellationToken = default)
    {
        string packageDir = Path.Combine(_voicePacksRoot, voiceId);
        if (!Directory.Exists(packageDir))
        {
            throw new DirectoryNotFoundException($"Voice pack not found: {voiceId}");
        }

        if (File.Exists(destinationZipPath))
        {
            File.Delete(destinationZipPath);
        }

        await Task.Run(() =>
        {
            ZipFile.CreateFromDirectory(packageDir, destinationZipPath, CompressionLevel.Optimal, false);
        }, cancellationToken);

        return destinationZipPath;
    }

    public async Task<VoicePackageInfo> CreateAndInstallPackageAsync(
        VoicePackageManifest manifest,
        string sourceModelPath,
        string? sourceConfigPath = null,
        string? sourceSamplePath = null,
        CancellationToken cancellationToken = default)
    {
        string packageDir = Path.Combine(_voicePacksRoot, manifest.Id);
        if (!Directory.Exists(packageDir))
        {
            Directory.CreateDirectory(packageDir);
        }

        manifest.ModelFileName = Path.GetFileName(sourceModelPath);
        string destModel = Path.Combine(packageDir, manifest.ModelFileName);
        File.Copy(sourceModelPath, destModel, true);

        if (!string.IsNullOrEmpty(sourceConfigPath) && File.Exists(sourceConfigPath))
        {
            manifest.ConfigFileName = Path.GetFileName(sourceConfigPath);
            File.Copy(sourceConfigPath, Path.Combine(packageDir, manifest.ConfigFileName), true);
        }

        if (!string.IsNullOrEmpty(sourceSamplePath) && File.Exists(sourceSamplePath))
        {
            manifest.SampleAudioFileName = Path.GetFileName(sourceSamplePath);
            File.Copy(sourceSamplePath, Path.Combine(packageDir, manifest.SampleAudioFileName), true);
        }

        string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(packageDir, "voice.json"), manifestJson, cancellationToken);

        return new VoicePackageInfo
        {
            Manifest = manifest,
            PackageDirectory = packageDir,
            ModelAbsolutePath = destModel,
            ConfigAbsolutePath = Path.Combine(packageDir, manifest.ConfigFileName),
            SampleAudioAbsolutePath = !string.IsNullOrEmpty(sourceSamplePath) ? Path.Combine(packageDir, Path.GetFileName(sourceSamplePath)) : null
        };
    }
}
