using SafeSpeak.Core.Audio;
using System.Security.Cryptography;

namespace SafeSpeak.Core.Tests;

public sealed class KokoroRuntimeSmokeTests
{
    [Fact]
    public async Task InstalledModel_SynthesizesAValidWave_WhenSmokeModelIsProvided()
    {
        string? modelPath = Environment.GetEnvironmentVariable("SAFESPEAK_KOKORO_MODEL_PATH");
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return;
        }

        Assert.True(File.Exists(modelPath), $"Kokoro smoke-test model does not exist: {modelPath}");
        await using (var model = File.OpenRead(modelPath))
        using (var sha256 = SHA256.Create())
        {
            Assert.Equal(
                KokoroModelManager.ModelSha256,
                Convert.ToHexString(await sha256.ComputeHashAsync(model)));
        }

        using var manager = new KokoroModelManager(Path.GetDirectoryName(modelPath));
        await using var output = new MemoryStream();
        await manager.SynthesizeAsync(
            "SafeSpeak Kokoro runtime test.",
            output,
            "kokoro:af_heart",
            rate: 0,
            CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(3));

        byte[] wave = output.ToArray();
        Assert.True(wave.Length > 44, "Kokoro returned an empty WAV payload.");
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
    }
}
