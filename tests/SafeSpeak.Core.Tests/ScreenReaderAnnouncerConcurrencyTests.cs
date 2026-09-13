using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Audio;

namespace SafeSpeak.Core.Tests;

public sealed class ScreenReaderAnnouncerConcurrencyTests
{
    [Fact]
    public async Task RapidInterrupt_NeverRunsConcurrentAudioStreams()
    {
        var audioRouter = new TrackingAudioRouter();
        var speechEngine = new SystemSpeechTtsEngine(() => new InstantWaveSynthesizer());
        using var announcer = new ScreenReaderAnnouncer(speechEngine, audioRouter);

        for (int i = 0; i < 20; i++)
        {
            announcer.Announce($"Step {i}", interrupt: true);
            await Task.Yield();
        }

        await Task.Delay(200);
        announcer.StopSpeaking();

        Assert.Equal(0, audioRouter.MaxConcurrentPlayback);
        Assert.True(audioRouter.PlaybackCount > 0);
    }

    [Fact]
    public async Task RapidInterrupt_DoesNotDeadlockOrHang()
    {
        var audioRouter = new TrackingAudioRouter();
        var speechEngine = new SystemSpeechTtsEngine(() => new InstantWaveSynthesizer());
        using var announcer = new ScreenReaderAnnouncer(speechEngine, audioRouter);

        Task rapidTasks = Task.Run(async () =>
        {
            for (int i = 0; i < 30; i++)
            {
                announcer.Announce($"Rapid navigation step {i}", interrupt: true);
                if (i % 5 == 0)
                {
                    await Task.Delay(10);
                }
            }
        });

        await rapidTasks.WaitAsync(TimeSpan.FromSeconds(5));
        announcer.StopSpeaking();
    }

    private sealed class InstantWaveSynthesizer : IWaveSpeechSynthesizer
    {
        public void Configure(Stream outputStream, string? voiceName, int rate, int volume)
        {
            outputStream.Write(new byte[100]);
        }

        public void Speak(string text)
        {
        }

        public void Cancel()
        {
        }

        public void ResetOutput()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TrackingAudioRouter : IAudioRouter
    {
        private int _currentConcurrent;
        private int _maxConcurrent;
        private int _playbackCount;

        public int MaxConcurrentPlayback => Volatile.Read(ref _maxConcurrent);
        public int PlaybackCount => Volatile.Read(ref _playbackCount);

        public event EventHandler? EndpointsChanged { add { } remove { } }

        public string? SelectedEndpointId => "default";

        public IReadOnlyList<AudioEndpointInfo> GetOutputEndpoints() => [];

        public void SelectEndpoint(string? endpointId) { }

        public async Task PlayWaveStreamAsync(
            Stream waveStream,
            float volume = 1,
            CancellationToken cancellationToken = default)
        {
            int current = Interlocked.Increment(ref _currentConcurrent);
            Interlocked.Increment(ref _playbackCount);

            if (current > 1)
            {
                // Concurrency violation: more than one audio stream playing simultaneously
                Interlocked.Exchange(ref _maxConcurrent, current);
            }

            try
            {
                // Simulate 50ms audio playback that responds to cancellation
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrent);
            }
        }

        public void Stop() { }

        public void Dispose() { }
    }
}
