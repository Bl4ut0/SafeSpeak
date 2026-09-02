using KokoroSharp;
using KokoroSharp.Processing;
using NAudio.Wave;
using System.Security.Cryptography;

namespace SafeSpeak.Core.Audio;

/// <summary>
/// Installs and runs Kokoro locally. The model is downloaded only after the user
/// explicitly requests it; voice embeddings ship with KokoroSharp.CPU.
/// </summary>
public sealed class KokoroModelManager : IDisposable
{
    public const string VoicePrefix = "kokoro:";
    public const string ModelDownloadUrl =
        "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx";
    public const string ModelSha256 =
        "0CFD5E79AAB70A3D8C1A57DC639835110DDB32C9F5FF4FDD1F4DB202EA43BB05";

    private readonly SemaphoreSlim _synthesisLock = new(1, 1);
    private KokoroWavSynthesizer? _synthesizer;

    public string ModelDirectory { get; }
    public string VoiceDirectory { get; }
    public string ModelPath => Path.Combine(ModelDirectory, "kokoro.onnx");
    public bool IsInstalled => File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 100_000_000;

    public static IReadOnlyList<VoiceInfo> EnglishVoices { get; } =
    [
        Voice("af_heart", "Heart", "en-US", "Female", "Warm, expressive American voice"),
        Voice("af_bella", "Bella", "en-US", "Female", "Clear, lively American voice"),
        Voice("af_nicole", "Nicole", "en-US", "Female", "Natural conversational American voice"),
        Voice("af_sarah", "Sarah", "en-US", "Female", "Balanced American narrator"),
        Voice("af_sky", "Sky", "en-US", "Female", "Bright American voice"),
        Voice("af_nova", "Nova", "en-US", "Female", "Modern American voice"),
        Voice("af_alloy", "Alloy", "en-US", "Female", "Even American voice"),
        Voice("af_aoede", "Aoede", "en-US", "Female", "Expressive American voice"),
        Voice("af_jessica", "Jessica", "en-US", "Female", "Friendly American voice"),
        Voice("af_kore", "Kore", "en-US", "Female", "Direct American voice"),
        Voice("af_river", "River", "en-US", "Female", "Calm American voice"),
        Voice("am_adam", "Adam", "en-US", "Male", "Deep American voice"),
        Voice("am_michael", "Michael", "en-US", "Male", "Natural American narrator"),
        Voice("am_liam", "Liam", "en-US", "Male", "Friendly American voice"),
        Voice("am_onyx", "Onyx", "en-US", "Male", "Strong American voice"),
        Voice("am_puck", "Puck", "en-US", "Male", "Energetic American voice"),
        Voice("am_echo", "Echo", "en-US", "Male", "Smooth American voice"),
        Voice("am_eric", "Eric", "en-US", "Male", "Clear American voice"),
        Voice("am_fenrir", "Fenrir", "en-US", "Male", "Characterful American voice"),
        Voice("bf_emma", "Emma", "en-GB", "Female", "Natural British voice"),
        Voice("bf_isabella", "Isabella", "en-GB", "Female", "Polished British voice"),
        Voice("bf_alice", "Alice", "en-GB", "Female", "Friendly British voice"),
        Voice("bf_lily", "Lily", "en-GB", "Female", "Soft British voice"),
        Voice("bm_george", "George", "en-GB", "Male", "Natural British narrator"),
        Voice("bm_daniel", "Daniel", "en-GB", "Male", "Clear British voice"),
        Voice("bm_fable", "Fable", "en-GB", "Male", "Expressive British voice"),
        Voice("bm_lewis", "Lewis", "en-GB", "Male", "Measured British voice")
    ];

    public KokoroModelManager(string? modelDirectory = null)
    {
        ModelDirectory = modelDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSpeak", "Models", "Kokoro");
        VoiceDirectory = Path.Combine(AppContext.BaseDirectory, "voices");
        Directory.CreateDirectory(ModelDirectory);
    }

    private static VoiceInfo Voice(string id, string name, string culture, string gender, string description) =>
        new(VoicePrefix + id, $"Kokoro — {name} ({culture})", "Kokoro Local Neural", culture, gender, description, true);

    public async Task InstallAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            progress?.Report(100);
            return;
        }

        string temporaryPath = ModelPath + ".download";
        try
        {
            long copied = 0;
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var response = await client.GetAsync(ModelDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long expected = response.Content.Headers.ContentLength ?? 330_000_000;
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 128,
                    true);
                byte[] buffer = new byte[1024 * 128];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    copied += read;
                    progress?.Report(Math.Min(99, copied * 100d / expected));
                }
                await destination.FlushAsync(cancellationToken);
            }

            if (copied < 100_000_000) throw new InvalidDataException("The Kokoro model download was incomplete.");

            await using (var downloadedModel = new FileStream(
                temporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 128,
                true))
            using (var sha256 = SHA256.Create())
            {
                string actualHash = Convert.ToHexString(
                    await sha256.ComputeHashAsync(downloadedModel, cancellationToken));
                if (!string.Equals(actualHash, ModelSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The downloaded Kokoro model failed integrity verification.");
                }
            }

            File.Move(temporaryPath, ModelPath, true);
            progress?.Report(100);
        }
        catch
        {
            try { File.Delete(temporaryPath); } catch { }
            throw;
        }
    }

    public async Task SynthesizeAsync(string text, Stream outputStream, string voiceId, int rate, CancellationToken cancellationToken)
    {
        if (!IsInstalled) throw new InvalidOperationException("Kokoro is not installed.");
        string voiceName = voiceId.StartsWith(VoicePrefix, StringComparison.OrdinalIgnoreCase)
            ? voiceId[VoicePrefix.Length..]
            : voiceId;

        await _synthesisLock.WaitAsync(cancellationToken);
        try
        {
            _synthesizer ??= new KokoroWavSynthesizer(ModelPath);
            // Use the packaged application base explicitly. Relying on the
            // library's implicit lookup can resolve relative to the launcher
            // rather than the MSIX payload in an identity-bearing process.
            KokoroVoiceManager.LoadVoicesFromPath(VoiceDirectory);
            var voice = KokoroVoiceManager.GetVoice(voiceName);
            var config = new KokoroTTSPipelineConfig
            {
                Speed = Math.Clamp(1f + (Math.Clamp(rate, -5, 5) * 0.06f), 0.7f, 1.3f)
            };
            byte[] pcm = await _synthesizer.SynthesizeAsync(text, voice, config).WaitAsync(cancellationToken);
            using var writer = new WaveFileWriter(new NonClosingStream(outputStream), new WaveFormat(24_000, 16, 1));
            await writer.WriteAsync(pcm, cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _synthesisLock.Release();
        }
    }

    public void Dispose()
    {
        _synthesizer?.Dispose();
        _synthesisLock.Dispose();
    }

    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { }
    }
}
