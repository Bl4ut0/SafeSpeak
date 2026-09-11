using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Models;
using Xunit;

namespace SafeSpeak.Core.Tests;

public sealed class TtsCadenceAndPaceTests
{
    [Theory]
    [InlineData(1000, false, 0, 1000)]
    [InlineData(1000, false, 1, 1000)]
    [InlineData(1000, false, 5, 1000)]
    [InlineData(1000, false, 12, 1000)]
    [InlineData(2000, true, 0, 2000)]
    [InlineData(2000, true, 1, 2000)]
    [InlineData(2000, true, 2, 1000)] // Half delay (>= 2)
    [InlineData(2000, true, 4, 1000)] // Half delay (< 5)
    [InlineData(2000, true, 5, 500)]  // Quarter delay (>= 5)
    [InlineData(2000, true, 9, 500)]  // Quarter delay (< 10)
    [InlineData(2000, true, 10, 0)]   // Zero delay (>= 10)
    [InlineData(2000, true, 50, 0)]   // Zero delay (>= 10)
    public void CalculateEffectiveInterMessageGap_ComputesCorrectCadence(
        int maxGap,
        bool adaptive,
        int pendingCount,
        int expectedGap)
    {
        int effective = TtsQueue.CalculateEffectiveInterMessageGap(maxGap, adaptive, pendingCount);
        Assert.Equal(expectedGap, effective);
    }

    [Fact]
    public async Task TtsQueue_SynthesizesAtFullScale_AndDelegatesVolumeToAudioRouter()
    {
        var engine = new TrackingTtsEngine();
        var router = new TrackingAudioRouter();
        await using var queue = new TtsQueue(engine, router)
        {
            SpeechVolume = 65,
            SpeechRate = 2,
            SelectedVoice = "test_voice"
        };
        queue.ArmAutomatic();

        var decision = Approved("Testing volume normalization across tiers.");
        var playbackFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.PlaybackFinished += (_, _) => playbackFinished.TrySetResult(true);

        queue.Enqueue(decision);

        await playbackFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Synthesis must always occur at 100 (nominal full-scale) to prevent double attenuation
        Assert.Equal(100, engine.LastSynthesizedVolume);
        Assert.Equal(2, engine.LastSynthesizedRate);
        Assert.Equal("test_voice", engine.LastSynthesizedVoiceId);

        // AudioRouter must receive the scaled float volume
        Assert.InRange(router.LastPlayedVolume, 0.64f, 0.66f);
    }

    [Fact]
    public async Task TtsQueue_HandlesSpeechRateExtremes_WithoutErrors()
    {
        var engine = new TrackingTtsEngine();
        var router = new TrackingAudioRouter();
        await using var queue = new TtsQueue(engine, router)
        {
            SpeechRate = -5
        };
        queue.ArmAutomatic();

        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.PlaybackFinished += (_, _) => finished.TrySetResult(true);

        queue.Enqueue(Approved("Slow pace test"));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(-5, engine.LastSynthesizedRate);
    }

    [Fact]
    public async Task TtsQueue_SwitchesVoiceTiersSeamlessly_DuringQueuePlayback()
    {
        var engine = new TrackingTtsEngine();
        var router = new TrackingAudioRouter();
        await using var queue = new TtsQueue(engine, router)
        {
            InterMessageGapMilliseconds = 0,
            AdaptiveInterMessageGap = false
        };
        queue.ArmAutomatic();

        string[] voicesToTest =
        [
            "Microsoft David Desktop",     // Level 1 SAPI
            "winrt:Microsoft David",       // Level 2 Windows Natural
            "kokoro:af_heart",             // Level 3 Kokoro Neural
            "pkg:custom_creator_pack"      // Level 4 Custom Package
        ];

        for (int i = 0; i < voicesToTest.Length; i++)
        {
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnFinished(object? s, ModerationDecision d) => finished.TrySetResult(true);
            queue.PlaybackFinished += OnFinished;

            queue.SelectedVoice = voicesToTest[i];
            queue.Enqueue(Approved($"Message for tier {i + 1}"));

            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.PlaybackFinished -= OnFinished;

            Assert.Equal(voicesToTest[i], engine.LastSynthesizedVoiceId);
        }

        Assert.Equal(4, engine.SynthesizedVoices.Count);
        Assert.Equal(4, router.PlaybackCount);
    }

    [Theory]
    [InlineData(100, 0, 1.0f)]
    [InlineData(80, 50, 1.20f)]
    [InlineData(100, 100, 2.0f)]
    [InlineData(0, 100, 0.0f)] // Mute safety: 0% volume stays 0% even with max boost
    [InlineData(50, 0, 0.5f)]
    [InlineData(50, 50, 0.75f)]
    public async Task TtsQueue_WithVolumeAndBoost_CalculatesEffectiveVolumeCorrectly(
        int volume,
        int boost,
        float expectedPlaybackVolume)
    {
        var engine = new TrackingTtsEngine();
        var router = new TrackingAudioRouter();
        await using var queue = new TtsQueue(engine, router)
        {
            SpeechVolume = volume,
            SpeechBoost = boost
        };
        queue.ArmAutomatic();

        var playbackFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.PlaybackFinished += (_, _) => playbackFinished.TrySetResult(true);

        queue.Enqueue(Approved("Testing boost volume calculation."));
        await playbackFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(100, engine.LastSynthesizedVolume);
        Assert.InRange(router.LastPlayedVolume, expectedPlaybackVolume - 0.01f, expectedPlaybackVolume + 0.01f);
    }

    [Fact]
    public void AppSettings_NormalizeForPersistence_MigratesLegacyVolumeOver100ToSpeechBoost()
    {
        var legacySettings = new AppSettings
        {
            SpeechVolume = 150,
            SpeechBoost = 0
        };

        legacySettings.NormalizeForPersistence();

        Assert.Equal(100, legacySettings.SpeechVolume);
        Assert.Equal(50, legacySettings.SpeechBoost);
    }

    [Fact]
    public void AppSettings_NormalizeForPersistence_PreservesStandardVolumeAndBoost()
    {
        var standardSettings = new AppSettings
        {
            SpeechVolume = 85,
            SpeechBoost = 25
        };

        standardSettings.NormalizeForPersistence();

        Assert.Equal(85, standardSettings.SpeechVolume);
        Assert.Equal(25, standardSettings.SpeechBoost);
    }

    private static ModerationDecision Approved(string text) => new()
    {
        Message = new ChatMessage { RawText = text, Author = "viewer" },
        Disposition = ModerationDisposition.Approved,
        SpokenText = text
    };

    private sealed class TrackingTtsEngine : ITtsEngine
    {
        public int LastSynthesizedVolume { get; private set; }
        public int LastSynthesizedRate { get; private set; }
        public string? LastSynthesizedVoiceId { get; private set; }
        public List<string?> SynthesizedVoices { get; } = [];

        public IReadOnlyList<VoiceInfo> GetAvailableVoices() =>
        [
            new("Microsoft David Desktop", "System — Microsoft David Desktop", "Windows Desktop SAPI", "en-US", "Male", "Legacy", false, ComputeLevel: 1),
            new("winrt:Microsoft David", "Natural — Microsoft David (en-US)", "Windows Natural", "en-US", "Male", "OneCore", true, ComputeLevel: 2),
            new("kokoro:af_heart", "Kokoro — Heart (en-US)", "Kokoro Neural", "en-US", "Female", "Kokoro", true, ComputeLevel: 3),
            new("pkg:custom_creator_pack", "Custom — Creator Pack (en-US)", "Custom Voice Package", "en-US", "Neutral", "Custom", true, ComputeLevel: 4)
        ];

        public Task SynthesizeToWaveStreamAsync(
            string text,
            Stream outputStream,
            string? voiceName = null,
            int rate = 0,
            int volume = 100,
            CancellationToken cancellationToken = default)
        {
            LastSynthesizedVolume = volume;
            LastSynthesizedRate = rate;
            LastSynthesizedVoiceId = voiceName;
            SynthesizedVoices.Add(voiceName);

            // Minimal valid 44-byte WAV header
            var header = new byte[44];
            header[0] = (byte)'R'; header[1] = (byte)'I'; header[2] = (byte)'F'; header[3] = (byte)'F';
            header[8] = (byte)'W'; header[9] = (byte)'A'; header[10] = (byte)'V'; header[11] = (byte)'E';
            outputStream.Write(header);
            return Task.CompletedTask;
        }

        public Task SpeakDirectAsync(
            string text,
            string? voiceName = null,
            int rate = 0,
            int volume = 100,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class TrackingAudioRouter : IAudioRouter
    {
        public float LastPlayedVolume { get; private set; }
        public int PlaybackCount { get; private set; }
        public string? SelectedEndpointId { get; set; }

        public event EventHandler? EndpointsChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<AudioEndpointInfo> GetOutputEndpoints() =>
            [new("default", "Default Audio Device", true, true)];

        public void SelectEndpoint(string? endpointId)
        {
            SelectedEndpointId = endpointId;
        }

        public Task PlayWaveStreamAsync(
            Stream waveStream,
            float volume = 1,
            CancellationToken cancellationToken = default)
        {
            LastPlayedVolume = volume;
            PlaybackCount++;
            return Task.CompletedTask;
        }

        public void Stop() { }
        public void Dispose() { }
    }
}
