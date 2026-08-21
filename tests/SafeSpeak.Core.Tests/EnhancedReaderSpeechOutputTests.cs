using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Audio;

namespace SafeSpeak.Core.Tests;

public sealed class EnhancedReaderSpeechOutputTests
{
    [Fact]
    public async Task Speak_UsesConfiguredVoiceAndPrivateRouter()
    {
        var engine = new RecordingTtsEngine();
        var router = new RecordingAudioRouter();
        await using var output = new EnhancedReaderSpeechOutput(engine, router)
        {
            VoiceId = "kokoro:af_heart",
            Rate = 3,
            Volume = 72
        };

        output.Speak("Reader voice test", interrupt: true);
        await router.PlaybackStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("Reader voice test", engine.Text);
        Assert.Equal("kokoro:af_heart", engine.VoiceId);
        Assert.Equal(3, engine.Rate);
        Assert.Equal(100, engine.Volume);
        Assert.Equal(0.72f, router.Volume, precision: 2);
    }

    [Fact]
    public async Task WarmCache_PersistsStaticPhraseForNextSession()
    {
        string cacheDirectory = Path.Combine(Path.GetTempPath(), "SafeSpeak-reader-cache-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var firstEngine = new RecordingTtsEngine();
            await using (var firstOutput = new EnhancedReaderSpeechOutput(
                             firstEngine,
                             new RecordingAudioRouter(),
                             cacheDirectory)
                         {
                             VoiceId = "kokoro:af_heart",
                             Rate = 4
                         })
            {
                firstOutput.WarmCache("Audio tab. selected tab.", persist: true);
                await firstEngine.SynthesisCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await WaitForCacheFileAsync(cacheDirectory);
            }

            var secondEngine = new RecordingTtsEngine();
            var secondRouter = new RecordingAudioRouter();
            await using (var secondOutput = new EnhancedReaderSpeechOutput(
                             secondEngine,
                             secondRouter,
                             cacheDirectory)
                         {
                             VoiceId = "kokoro:af_heart",
                             Rate = 4
                         })
            {
                bool cacheHit = secondOutput.TrySpeakCached("Audio tab. selected tab.", interrupt: true);
                await secondRouter.PlaybackStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

                Assert.True(cacheHit);
                Assert.Equal(0, secondEngine.SynthesisCount);
            }
        }
        finally
        {
            if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    private static async Task WaitForCacheFileAsync(string cacheDirectory)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(cacheDirectory) && Directory.EnumerateFiles(cacheDirectory, "*.wav").Any()) return;
            await Task.Delay(20);
        }

        throw new TimeoutException("The reader phrase was not persisted to the test cache.");
    }

    private sealed class RecordingTtsEngine : ITtsEngine
    {
        public string? Text { get; private set; }
        public string? VoiceId { get; private set; }
        public int Rate { get; private set; }
        public int Volume { get; private set; }
        public int SynthesisCount { get; private set; }
        public TaskCompletionSource SynthesisCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<VoiceInfo> GetAvailableVoices() => Array.Empty<VoiceInfo>();

        public async Task SynthesizeToWaveStreamAsync(
            string text,
            Stream outputStream,
            string? voiceId = null,
            int rate = 0,
            int volume = 100,
            CancellationToken cancellationToken = default)
        {
            Text = text;
            VoiceId = voiceId;
            Rate = rate;
            Volume = volume;
            SynthesisCount++;
            await outputStream.WriteAsync(new byte[64], cancellationToken);
            SynthesisCompleted.TrySetResult();
        }

        public Task SpeakDirectAsync(
            string text,
            string? voiceId = null,
            int rate = 0,
            int volume = 100,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class RecordingAudioRouter : IAudioRouter
    {
        public TaskCompletionSource PlaybackStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public float Volume { get; private set; }
        public string? SelectedEndpointId { get; private set; }

        public IReadOnlyList<AudioEndpointInfo> GetOutputEndpoints() => Array.Empty<AudioEndpointInfo>();

        public void SelectEndpoint(string? endpointId) => SelectedEndpointId = endpointId;

        public Task PlayWaveStreamAsync(
            Stream waveStream,
            float volume = 1,
            CancellationToken cancellationToken = default)
        {
            Volume = volume;
            PlaybackStarted.TrySetResult();
            return Task.CompletedTask;
        }

        public void Stop() { }
        public void Dispose() { }
    }
}
