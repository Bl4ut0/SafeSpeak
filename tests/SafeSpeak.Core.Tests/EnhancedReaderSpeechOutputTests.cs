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

    private sealed class RecordingTtsEngine : ITtsEngine
    {
        public string? Text { get; private set; }
        public string? VoiceId { get; private set; }
        public int Rate { get; private set; }
        public int Volume { get; private set; }

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
            await outputStream.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
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
