using SafeSpeak.Core.AI;
using SafeSpeak.Core.Diagnostics;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;
using SafeSpeak.Core.Audio;

namespace SafeSpeak.Core.Tests;

public sealed class SessionCacheTests
{
    [Fact]
    public void CacheHonorsCostLruAndExpiry()
    {
        var clock = new TestClock();
        var cache = new BoundedMemoryCache<string, string>(2, TimeSpan.FromMinutes(1), clock);
        cache.Set("a", "a"); cache.Set("b", "b");
        Assert.True(cache.TryGet("a", out _)); cache.Set("c", "c");
        Assert.False(cache.TryGet("b", out _));
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.False(cache.TryGet("a", out _));
        cache.Set("huge", "huge", 3); Assert.False(cache.TryGet("huge", out _));
    }

    [Fact]
    public async Task NameScoresReuseInferenceButApplyChangedRulesAndThresholds()
    {
        var classifier = new CountingClassifier();
        var config = new ModerationConfig { UserCooldownSeconds = 0, IntentModerationLevel = 1 };
        using var pipeline = new ModerationPipeline(config, intentClassifier: classifier);
        ChatMessage message = new() { Author = "viewer", AuthorDisplayName = "Friendly Viewer", RawText = "Good play" };
        await pipeline.ProcessMessageAsync(message);
        await pipeline.ProcessMessageAsync(message with { RawText = "Nice work" });
        Assert.Equal(1, classifier.NameCalls);
        config.IntentModerationLevel = 4;
        var strict = await pipeline.ProcessMessageAsync(message with { RawText = "Thank you" });
        Assert.Equal("A viewer", strict.SafeAuthorDisplayName);
        Assert.Equal(1, classifier.NameCalls);
        config.CustomBlockedTerms.Add("friendly");
        var blocked = await pipeline.ProcessMessageAsync(message);
        Assert.DoesNotContain("Friendly Viewer", blocked.SpokenText);
        var replacement = new CountingClassifier();
        config.CustomBlockedTerms.Clear(); pipeline.SetIntentClassifier(replacement);
        await pipeline.ProcessMessageAsync(message);
        Assert.Equal(1, replacement.NameCalls);
    }

    [Fact]
    public async Task SpeechCacheSurvivesDisarmButSeparatesRateAndVoice()
    {
        var engine = new WaveEngine();
        await using var queue = new TtsQueue(engine, new MockAudioRouter());
        var decision = new ModerationDecision { Message = new ChatMessage { RawText = "Ready" }, SpokenText = "Ready" };
        async Task Play()
        {
            queue.ArmAutomatic(); queue.UseManualAdvance();
            Assert.True(queue.Enqueue(decision)); Assert.True(await queue.PlayNextManualAsync());
        }
        await Play(); queue.Disarm(); await Play(); Assert.Equal(1, engine.Calls);
        queue.SpeechRate = 1; await Play(); Assert.Equal(2, engine.Calls);
        queue.SelectedVoice = "different"; await Play(); Assert.Equal(3, engine.Calls);
        queue.ClearSpeechCache(); await Play(); Assert.Equal(4, engine.Calls);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void DiagnosticHostIdPersistsThroughRestart()
    {
        string root = Path.Combine(Path.GetTempPath(), "SafeSpeakHostIdTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "settings.json");
        try
        {
            AppSettings settings = AppSettings.Load(path);
            string host = settings.DiagnosticHostId;
            Assert.True(Guid.TryParse(host, out _)); Assert.True(settings.TrySave(out _));
            Assert.Equal(host, AppSettings.Load(path).DiagnosticHostId);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class CountingClassifier : IIntentClassifier
    {
        public int NameCalls;
        public string ModelName => "test";
        public bool IsModelLoaded => true;
        public Task<IntentClassificationResult> ClassifyAsync(string text, CancellationToken cancellationToken = default)
        {
            bool name = text.Equals("Friendly Viewer", StringComparison.OrdinalIgnoreCase);
            if (name) NameCalls++;
            return Task.FromResult(new IntentClassificationResult { ToxicityScore = name ? 0.5 : 0, ModelUsed = "test" });
        }
        public void Dispose() { }
    }
    private sealed class WaveEngine : ITtsEngine
    {
        public int Calls;
        public IReadOnlyList<VoiceInfo> GetAvailableVoices() => [];
        public Task SynthesizeToWaveStreamAsync(string text, Stream outputStream, string? voiceId = null, int rate = 0, int volume = 100, CancellationToken cancellationToken = default)
        { Calls++; outputStream.Write(new byte[100]); return Task.CompletedTask; }
        public Task SpeakDirectAsync(string text, string? voiceId = null, int rate = 0, int volume = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Stop() { }
        public void Dispose() { }
    }
}
