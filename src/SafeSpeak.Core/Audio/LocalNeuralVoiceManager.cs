namespace SafeSpeak.Core.Audio;

public sealed record NeuralVoiceCatalogItem(
    string Id,
    string DisplayName,
    string Culture,
    string Gender,
    string Description,
    string ModelUrl,
    string ConfigUrl,
    long ApproximateSizeBytes
);

public sealed class LocalNeuralVoiceManager
{
    private readonly string _voicesDirectory;

    public string VoicesDirectory => _voicesDirectory;

    public static readonly IReadOnlyList<NeuralVoiceCatalogItem> AvailableCatalog = new List<NeuralVoiceCatalogItem>
    {
        new(
            "en_US-amy-medium",
            "Amy HD (Natural Neural Female)",
            "en-US",
            "Female",
            "Studio-quality warm natural voice for high-clarity streaming.",
            "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/amy/medium/en_US-amy-medium.onnx",
            "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/amy/medium/en_US-amy-medium.onnx.json",
            63_000_000
        ),
        new(
            "en_US-ryan-medium",
            "Ryan Studio (Natural Neural Male)",
            "en-US",
            "Male",
            "Crisp energetic studio male voice designed for esports and gaming streams.",
            "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/ryan/medium/en_US-ryan-medium.onnx",
            "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/ryan/medium/en_US-ryan-medium.onnx.json",
            64_000_000
        ),
        new(
            "en_US-lessac-medium",
            "Lessac Broadcaster (Natural Neural Neutral)",
            "en-US",
            "Neutral",
            "Professional broadcast narrator voice with high syllable articulation.",
            "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx",
            "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx.json",
            63_000_000
        )
    };

    public LocalNeuralVoiceManager(string? customDirectory = null)
    {
        _voicesDirectory = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSpeak",
            "Voices"
        );

        if (!Directory.Exists(_voicesDirectory))
        {
            Directory.CreateDirectory(_voicesDirectory);
        }
    }

    public IReadOnlyList<string> GetInstalledVoiceIds()
    {
        var list = new List<string>();
        if (!Directory.Exists(_voicesDirectory)) return list;

        foreach (var file in Directory.GetFiles(_voicesDirectory, "*.onnx"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            list.Add(name);
        }

        return list;
    }

    public bool IsVoiceInstalled(string voiceId)
    {
        string modelPath = Path.Combine(_voicesDirectory, $"{voiceId}.onnx");
        return File.Exists(modelPath) && new FileInfo(modelPath).Length > 1000;
    }

    public async Task<bool> DownloadVoicePackageAsync(
        NeuralVoiceCatalogItem item,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string targetModel = Path.Combine(_voicesDirectory, $"{item.Id}.onnx");
        string targetConfig = Path.Combine(_voicesDirectory, $"{item.Id}.onnx.json");

        try
        {
            using var client = new HttpClient();

            // Download JSON config
            var configBytes = await client.GetByteArrayAsync(item.ConfigUrl, cancellationToken);
            await File.WriteAllBytesAsync(targetConfig, configBytes, cancellationToken);

            // Download ONNX model with progress tracking
            using var response = await client.GetAsync(item.ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? item.ApproximateSizeBytes;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(targetModel + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[16384];
            long totalRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;
                if (totalBytes > 0)
                {
                    progress?.Report((double)totalRead / totalBytes * 100.0);
                }
            }

            await fileStream.FlushAsync(cancellationToken);
            fileStream.Close();

            if (File.Exists(targetModel)) File.Delete(targetModel);
            File.Move(targetModel + ".tmp", targetModel);

            return true;
        }
        catch
        {
            if (File.Exists(targetModel + ".tmp"))
            {
                try { File.Delete(targetModel + ".tmp"); } catch { }
            }
            return false;
        }
    }
}
