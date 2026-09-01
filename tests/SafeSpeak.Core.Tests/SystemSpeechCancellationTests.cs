using SafeSpeak.Core.Audio;

namespace SafeSpeak.Core.Tests;

public sealed class SystemSpeechCancellationTests
{
    [Fact]
    public async Task ModularStop_CancelsExactInFlightWaveSynthesizerOnce()
    {
        using var testContext = new ModularEngineTestContext();
        var synthesizer = new BlockingWaveSynthesizer();
        using var engine = testContext.CreateEngine(() => synthesizer);
        using var output = new MemoryStream();

        Task synthesis = engine.SynthesizeToWaveStreamAsync("This blocks until stopped.", output);
        await synthesizer.Started.WaitAsync(TestTimeout);

        engine.Stop();
        engine.Stop();

        await synthesis.WaitAsync(TestTimeout);
        Assert.Equal(1, synthesizer.CancelCount);
        Assert.Equal(1, synthesizer.DisposeCount);
        Assert.Equal(1, synthesizer.ResetOutputCount);
    }

    [Fact]
    public async Task SystemSpeechCancellationToken_CancelsInFlightWaveSynthesis()
    {
        var synthesizer = new BlockingWaveSynthesizer();
        using var engine = new SystemSpeechTtsEngine(() => synthesizer);
        using var output = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        Task synthesis = engine.SynthesizeToWaveStreamAsync(
            "Cancellation must reach the active synthesizer.",
            output,
            cancellationToken: cancellation.Token);
        await synthesizer.Started.WaitAsync(TestTimeout);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => synthesis.WaitAsync(TestTimeout));
        Assert.Equal(1, synthesizer.CancelCount);
        Assert.Equal(1, synthesizer.DisposeCount);
    }

    [Fact]
    public async Task DisposeDuringWaveSynthesis_CancelsWithoutHangingAndIsIdempotent()
    {
        var synthesizer = new BlockingWaveSynthesizer();
        var engine = new SystemSpeechTtsEngine(() => synthesizer);
        using var output = new MemoryStream();
        Task synthesis = engine.SynthesizeToWaveStreamAsync("Dispose must stop this.", output);
        await synthesizer.Started.WaitAsync(TestTimeout);

        engine.Dispose();
        engine.Dispose();

        await synthesis.WaitAsync(TestTimeout);
        Assert.Equal(1, synthesizer.CancelCount);
        Assert.Equal(1, synthesizer.DisposeCount);
        engine.Stop();
    }

    [Fact]
    public async Task StopDuringConfiguration_PreventsSpeechFromStarting()
    {
        var synthesizer = new ConfiguringWaveSynthesizer();
        using var engine = new SystemSpeechTtsEngine(() => synthesizer);
        using var output = new MemoryStream();
        Task synthesis = engine.SynthesizeToWaveStreamAsync("Must never start.", output);
        await synthesizer.Configuring.WaitAsync(TestTimeout);

        engine.Stop();
        synthesizer.ReleaseConfiguration();

        await synthesis.WaitAsync(TestTimeout);
        Assert.Equal(1, synthesizer.CancelCount);
        Assert.Equal(0, synthesizer.SpeakCount);
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(2);

    private sealed class BlockingWaveSynthesizer : IWaveSpeechSynthesizer
    {
        private readonly ManualResetEventSlim _cancelled = new(initialState: false);
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _cancelCount;
        private int _disposeCount;
        private int _resetOutputCount;

        public Task Started => _started.Task;
        public int CancelCount => Volatile.Read(ref _cancelCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public int ResetOutputCount => Volatile.Read(ref _resetOutputCount);

        public void Configure(Stream outputStream, string? voiceName, int rate, int volume)
        {
        }

        public void Speak(string text)
        {
            _started.TrySetResult();
            if (!_cancelled.Wait(TestTimeout))
            {
                throw new TimeoutException("The test synthesizer was not cancelled.");
            }
        }

        public void Cancel()
        {
            Interlocked.Increment(ref _cancelCount);
            _cancelled.Set();
        }

        public void ResetOutput()
        {
            Interlocked.Increment(ref _resetOutputCount);
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            _cancelled.Dispose();
        }
    }

    private sealed class ConfiguringWaveSynthesizer : IWaveSpeechSynthesizer
    {
        private readonly TaskCompletionSource _configuring =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _configurationReleased = new(initialState: false);
        private int _cancelCount;
        private int _speakCount;

        public Task Configuring => _configuring.Task;
        public int CancelCount => Volatile.Read(ref _cancelCount);
        public int SpeakCount => Volatile.Read(ref _speakCount);

        public void Configure(Stream outputStream, string? voiceName, int rate, int volume)
        {
            _configuring.TrySetResult();
            if (!_configurationReleased.Wait(TestTimeout))
            {
                throw new TimeoutException("The test did not release configuration.");
            }
        }

        public void ReleaseConfiguration() => _configurationReleased.Set();

        public void Speak(string text) => Interlocked.Increment(ref _speakCount);

        public void Cancel() => Interlocked.Increment(ref _cancelCount);

        public void ResetOutput()
        {
        }

        public void Dispose() => _configurationReleased.Dispose();
    }

    private sealed class ModularEngineTestContext : IDisposable
    {
        private readonly string _modelDirectory = Path.Combine(
            Path.GetTempPath(),
            "SafeSpeak.Tests",
            Guid.NewGuid().ToString("N"));

        public ModularTtsEngine CreateEngine(Func<IWaveSpeechSynthesizer> factory) =>
            new(new KokoroModelManager(_modelDirectory), factory);

        public void Dispose()
        {
            try { Directory.Delete(_modelDirectory, recursive: true); } catch { }
        }
    }
}
