using System.Text;
using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Audio.VoiceFramework;

namespace SafeSpeak.Core.Tests;

public sealed class ModularTtsEngineCustomPackageTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"SafeSpeakModularTests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task ModularTtsEngine_WithVoicePackageManager_ListsCustomVoices()
    {
        Directory.CreateDirectory(_testRoot);
        string installedDir = Path.Combine(_testRoot, "installed");
        var packageManager = new VoicePackageManager(installedDir);

        string tempModel = Path.Combine(_testRoot, "dummy.onnx");
        string tempConfig = Path.Combine(_testRoot, "dummy.onnx.json");
        await File.WriteAllTextAsync(tempModel, "dummy-model-content");
        await File.WriteAllTextAsync(tempConfig, "{}");

        var manifest = new VoicePackageManifest
        {
            Id = "streamer-custom-voice",
            DisplayName = "Streamer Custom Voice",
            Culture = "en-US",
            Gender = "Female",
            Description = "Custom low-resource voice"
        };

        await packageManager.CreateAndInstallPackageAsync(manifest, tempModel, tempConfig);

        using var engine = new ModularTtsEngine(
            kokoroManager: null,
            voicePackageManager: packageManager,
            waveSynthesizerFactory: () => new DummyWaveSynthesizer());

        var voices = engine.GetAvailableVoices();
        var customVoice = voices.FirstOrDefault(v => v.Id == "pack:streamer-custom-voice");

        Assert.NotNull(customVoice);
        Assert.Equal("Custom — Streamer Custom Voice (en-US)", customVoice.DisplayName);
        Assert.Equal("Custom Voice Package", customVoice.Provider);
        Assert.True(customVoice.IsNaturalNeural);
    }

    [Fact]
    public async Task ModularTtsEngine_DeleteVoicePackage_RemovesFromVoices()
    {
        Directory.CreateDirectory(_testRoot);
        string installedDir = Path.Combine(_testRoot, "installed");
        var packageManager = new VoicePackageManager(installedDir);

        string tempModel = Path.Combine(_testRoot, "model.onnx");
        string tempConfig = Path.Combine(_testRoot, "model.onnx.json");
        await File.WriteAllTextAsync(tempModel, "dummy-model");
        await File.WriteAllTextAsync(tempConfig, "{}");

        var manifest = new VoicePackageManifest
        {
            Id = "temp-pack",
            DisplayName = "Temp Pack"
        };

        await packageManager.CreateAndInstallPackageAsync(manifest, tempModel, tempConfig);

        using var engine = new ModularTtsEngine(
            kokoroManager: null,
            voicePackageManager: packageManager,
            waveSynthesizerFactory: () => new DummyWaveSynthesizer());

        Assert.Contains(engine.GetAvailableVoices(), v => v.Id == "pack:temp-pack");

        bool deleted = packageManager.DeletePackage("temp-pack");
        Assert.True(deleted);

        Assert.DoesNotContain(engine.GetAvailableVoices(), v => v.Id == "pack:temp-pack");
    }

    [Fact]
    public async Task ModularTtsEngine_SynthesizeVoicePackage_PlaysSampleAudio()
    {
        Directory.CreateDirectory(_testRoot);
        string installedDir = Path.Combine(_testRoot, "installed");
        var packageManager = new VoicePackageManager(installedDir);

        string tempModel = Path.Combine(_testRoot, "model.onnx");
        string tempConfig = Path.Combine(_testRoot, "model.onnx.json");
        string tempSample = Path.Combine(_testRoot, "sample.wav");
        byte[] sampleBytes = Encoding.UTF8.GetBytes("RIFF-SAMPLE-AUDIO-DATA");

        await File.WriteAllTextAsync(tempModel, "model");
        await File.WriteAllTextAsync(tempConfig, "{}");
        await File.WriteAllBytesAsync(tempSample, sampleBytes);

        var manifest = new VoicePackageManifest
        {
            Id = "sample-voice",
            DisplayName = "Sample Voice",
            SampleAudioFileName = "sample.wav"
        };

        await packageManager.CreateAndInstallPackageAsync(manifest, tempModel, tempConfig, tempSample);

        using var engine = new ModularTtsEngine(
            kokoroManager: null,
            voicePackageManager: packageManager,
            waveSynthesizerFactory: () => new DummyWaveSynthesizer());

        using var output = new MemoryStream();
        await engine.SynthesizeToWaveStreamAsync("Testing voice sample", output, "pack:sample-voice");

        byte[] result = output.ToArray();
        Assert.Equal(sampleBytes, result);
    }

#if WINDOWS
    [Fact]
    public void ModularTtsEngine_ListsWindowsNaturalLevel2Voices()
    {
        using var engine = new ModularTtsEngine(
            kokoroManager: null,
            voicePackageManager: null,
            waveSynthesizerFactory: () => new DummyWaveSynthesizer());

        var voices = engine.GetAvailableVoices();
        var naturalVoices = voices.Where(v => v.ComputeLevel == 2 && v.Id.StartsWith(ModularTtsEngine.WinRtVoicePrefix)).ToList();

        Assert.NotEmpty(naturalVoices);
        Assert.All(naturalVoices, v =>
        {
            Assert.StartsWith("Natural — ", v.DisplayName);
            Assert.Equal("Windows Natural", v.Provider);
            Assert.True(v.IsNaturalNeural);
            Assert.Equal(2, v.ComputeLevel);
        });
    }

    [Fact]
    public async Task ModularTtsEngine_SynthesizesWindowsNaturalVoiceToWaveStream()
    {
        using var engine = new ModularTtsEngine(
            kokoroManager: null,
            voicePackageManager: null,
            waveSynthesizerFactory: () => new DummyWaveSynthesizer());

        var voices = engine.GetAvailableVoices();
        var naturalVoice = voices.FirstOrDefault(v => v.ComputeLevel == 2 && v.Id.StartsWith(ModularTtsEngine.WinRtVoicePrefix));
        Assert.NotNull(naturalVoice);

        using var output = new MemoryStream();
        await engine.SynthesizeToWaveStreamAsync("SafeSpeak test phrase.", output, naturalVoice.Id, rate: 0, volume: 100);

        Assert.True(output.Length > 44, "Synthesized wave stream must contain at least WAV header and audio data.");
        byte[] bytes = output.ToArray();
        Assert.Equal((byte)'R', bytes[0]);
        Assert.Equal((byte)'I', bytes[1]);
        Assert.Equal((byte)'F', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }
#endif

    private sealed class DummyWaveSynthesizer : IWaveSpeechSynthesizer
    {
        public void Configure(Stream outputStream, string? voiceId, int rate, int volume) { }
        public void Speak(string text) { }
        public void Cancel() { }
        public void ResetOutput() { }
        public void Dispose() { }
    }
}
