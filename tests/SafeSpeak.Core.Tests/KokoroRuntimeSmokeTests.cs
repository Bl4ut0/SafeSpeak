using SafeSpeak.Core.Audio;
using System.Security.Cryptography;

namespace SafeSpeak.Core.Tests;

public sealed class KokoroRuntimeSmokeTests
{
    [Fact]
    public void PackagedVoiceAssets_AreStagedInWritableApplicationData()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "SafeSpeak.Tests", Guid.NewGuid().ToString("N"));
        string modelDirectory = Path.Combine(testRoot, "model");
        string sourceDirectory = Path.Combine(testRoot, "package", "voices");
        Directory.CreateDirectory(sourceDirectory);

        try
        {
            foreach (VoiceInfo voice in KokoroModelManager.EnglishVoices)
            {
                string voiceName = voice.Id[KokoroModelManager.VoicePrefix.Length..];
                string sourcePath = Path.Combine(sourceDirectory, voiceName + ".npy");
                File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);
                File.SetAttributes(sourcePath, File.GetAttributes(sourcePath) | FileAttributes.ReadOnly);
            }

            using var manager = new KokoroModelManager(modelDirectory, sourceDirectory);
            manager.EnsureVoiceAssetsAreAccessible();

            Assert.Equal(Path.Combine(modelDirectory, "voices"), manager.VoiceDirectory);
            Assert.NotEqual(sourceDirectory, manager.VoiceDirectory);
            foreach (VoiceInfo voice in KokoroModelManager.EnglishVoices)
            {
                string voiceName = voice.Id[KokoroModelManager.VoicePrefix.Length..];
                string stagedPath = Path.Combine(manager.VoiceDirectory, voiceName + ".npy");
                Assert.True(File.Exists(stagedPath));
                Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(stagedPath));
                Assert.False((File.GetAttributes(stagedPath) & FileAttributes.ReadOnly) != 0);
            }
        }
        finally
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(testRoot, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(testRoot, recursive: true);
            }
            catch { }
        }
    }

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
