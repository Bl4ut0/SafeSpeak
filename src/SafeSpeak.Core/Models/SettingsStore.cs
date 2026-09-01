using System.Text;
using System.Text.Json;

namespace SafeSpeak.Core.Models;

/// <summary>
/// Owns durable AppSettings persistence. Writes are flushed to a unique file
/// in the destination directory before atomic replacement. A prior valid
/// primary is retained as a backup and used when the primary cannot be read.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();

    public SettingsStore(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        SettingsFilePath = Path.GetFullPath(settingsFilePath);
        BackupFilePath = SettingsFilePath + ".bak";
    }

    public string SettingsFilePath { get; }

    public string BackupFilePath { get; }

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (TryLoadFile(SettingsFilePath, out AppSettings? primary) &&
                primary is not null)
            {
                return primary;
            }

            if (TryLoadFile(BackupFilePath, out AppSettings? backup) &&
                backup is not null)
            {
                backup.WasRecoveredFromBackup = true;
                return backup;
            }

            return AppSettings.CreateDefaultForPath(SettingsFilePath);
        }
    }

    public bool TrySave(AppSettings settings, out string? error)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            string? temporaryPath = null;
            try
            {
                settings.NormalizeForPersistence();
                settings.SettingsSchemaVersion =
                    AppSettings.CurrentSettingsSchemaVersion;

                string directory =
                    Path.GetDirectoryName(SettingsFilePath)
                    ?? throw new InvalidOperationException(
                        "The settings path has no parent directory.");
                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(SettingsFilePath)}.{Guid.NewGuid():N}.tmp");

                WriteFlushedTemporaryFile(temporaryPath, settings);
                _ = LoadValidatedJson(temporaryPath);

                if (File.Exists(SettingsFilePath))
                {
                    string? backupPath = settings.WasRecoveredFromBackup
                        ? null
                        : BackupFilePath;
                    File.Replace(
                        temporaryPath,
                        SettingsFilePath,
                        backupPath,
                        ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporaryPath, SettingsFilePath);
                }

                temporaryPath = null;
                settings.AttachToPath(SettingsFilePath);
                settings.WasRecoveredFromBackup = false;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch
                    {
                        // A failed cleanup must not hide the persistence error.
                    }
                }
            }
        }
    }

    private bool TryLoadFile(string path, out AppSettings? settings)
    {
        settings = null;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            settings = LoadValidatedJson(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private AppSettings LoadValidatedJson(string path)
    {
        string json = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "SafeSpeak settings must contain a JSON object.");
        }

        if (root.TryGetProperty(
                nameof(AppSettings.SettingsSchemaVersion),
                out JsonElement schemaElement))
        {
            if (!schemaElement.TryGetInt32(out int schemaVersion) ||
                schemaVersion < 0)
            {
                throw new InvalidDataException(
                    "SafeSpeak settings contain an invalid schema version.");
            }

            if (schemaVersion > AppSettings.CurrentSettingsSchemaVersion)
            {
                throw new InvalidDataException(
                    "These SafeSpeak settings were created by a newer unsupported version.");
            }
        }

        AppSettings? loaded =
            JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
        if (loaded is null)
        {
            throw new InvalidDataException(
                "SafeSpeak settings could not be decoded.");
        }

        loaded.ApplyMigrations(root);
        loaded.NormalizeForPersistence();
        loaded.SettingsSchemaVersion =
            AppSettings.CurrentSettingsSchemaVersion;
        loaded.AttachToPath(SettingsFilePath);
        return loaded;
    }

    private static void WriteFlushedTemporaryFile(
        string temporaryPath,
        AppSettings settings)
    {
        using var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        using (var writer = new StreamWriter(
                   stream,
                   new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                   bufferSize: 4096,
                   leaveOpen: true))
        {
            string json = JsonSerializer.Serialize(settings, SerializerOptions);
            writer.Write(json);
            writer.Flush();
        }

        stream.Flush(flushToDisk: true);
    }
}
