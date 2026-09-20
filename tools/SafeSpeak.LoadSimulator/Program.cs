using System.Diagnostics;
using System.Text.Json;
using SafeSpeak.Core.AI;
using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Diagnostics;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

const int intentIterations = 100;
const int moderationIterations = 100;
string defaultKokoroDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SafeSpeak", "Models", "Kokoro");
string kokoroDirectory = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))
    ?? defaultKokoroDirectory;
bool skipKokoro = args.Contains("--skip-kokoro", StringComparer.OrdinalIgnoreCase);
bool pacedOnly = args.Contains("--paced-only", StringComparer.OrdinalIgnoreCase);
bool speechOnly = args.Contains("--speech-only", StringComparer.OrdinalIgnoreCase);
string? threadArgument = args.FirstOrDefault(a => a.StartsWith("--speech-threads=", StringComparison.OrdinalIgnoreCase));
int? speechThreads = threadArgument is null ? null : int.Parse(threadArgument.Split('=')[1]);
bool noSpin = args.Contains("--no-spin", StringComparer.OrdinalIgnoreCase);
bool noPlayback = args.Contains("--no-playback", StringComparer.OrdinalIgnoreCase);
bool queuePlayback = args.Contains("--queue-playback", StringComparer.OrdinalIgnoreCase);

var report = new LoadReport
{
    TimestampUtc = DateTimeOffset.UtcNow,
    Machine = new MachineInfo
    {
        OperatingSystem = Environment.OSVersion.ToString(),
        ProcessorCount = Environment.ProcessorCount,
        Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
    }
};

Console.WriteLine("SafeSpeak load simulation");
Console.WriteLine($"Logical processors available to process: {Environment.ProcessorCount}");

using var classifier = new LocalOnnxIntentClassifier();
Console.WriteLine(classifier.AvailabilityMessage);
if (!classifier.IsModelLoaded)
{
    Console.Error.WriteLine("The real MiniLM model is unavailable; stopping because fallback timings would be misleading.");
    return 2;
}

string[] samples =
[
    "That was an excellent play and the timing was perfect.",
    "Could you explain the route you used in this level?",
    "I disagree with that choice but I hope the next match goes well.",
    "This stream is fucking amazing!",
    "You are an idiot and nobody wants you here.",
    "I will find you after the stream and hurt you.",
    "Thanks for making the interface work with a screen reader.",
    "Please check the audio level because the music is a little loud."
];

await classifier.ClassifyAsync(samples[0]);

if (!pacedOnly && !speechOnly)
{
report.Results.Add(await MeasureAsync(
    "Intent inference, sequential",
    intentIterations,
    async () =>
    {
        for (int index = 0; index < intentIterations; index++)
        {
            await classifier.ClassifyAsync(samples[index % samples.Length]);
        }
    }));

report.Results.Add(await MeasureAsync(
    "Intent inference, four callers",
    intentIterations,
    () => Parallel.ForEachAsync(
        Enumerable.Range(0, intentIterations),
        new ParallelOptions { MaxDegreeOfParallelism = 4 },
        async (index, cancellationToken) =>
            await classifier.ClassifyAsync(samples[index % samples.Length], cancellationToken))));
}

var moderationConfig = new ModerationConfig
{
    UserCooldownSeconds = 0,
    MessageRateLimitEnabled = false,
    AudienceMode = AudienceMode.All,
    IntentModerationLevel = 2,
    EnglishOnly = true
};
using var pipeline = new ModerationPipeline(moderationConfig, intentClassifier: classifier);

if (pacedOnly)
{
    report.Notes.Add("Process-only MiniLM moderation simulation: excludes WPF UI, connectors, Qwen and audio. CPU is normalized across logical processors; pacing is included in the measurement window.");
    report.Results.Add(await MeasureAsync("Idle baseline", 0, () => Task.Delay(TimeSpan.FromSeconds(10))));
    foreach (int rate in new[] { 1, 5, 10 })
    {
        var latencies = new List<double>();
        int count = rate * 10;
        LoadResult result = await MeasureAsync($"MiniLM moderation, {rate} messages/sec", count, async () =>
        {
            Stopwatch schedule = Stopwatch.StartNew();
            for (int index = 0; index < count; index++)
            {
                double wait = index * 1000.0 / rate - schedule.Elapsed.TotalMilliseconds;
                if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait));
                Stopwatch processing = Stopwatch.StartNew();
                await pipeline.ProcessMessageAsync(Message(index + rate * 1000, samples[index % samples.Length]));
                latencies.Add(processing.Elapsed.TotalMilliseconds);
            }
            double remaining = 10_000 - schedule.Elapsed.TotalMilliseconds;
            if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining));
        });
        result.AverageProcessingMilliseconds = latencies.Average();
        result.P95ProcessingMilliseconds = latencies.Order().ElementAt((int)Math.Ceiling(count * 0.95) - 1);
        report.Results.Add(result);
    }
}
else if (!speechOnly)
{
report.Results.Add(await MeasureAsync(
    "Moderation pipeline, normal chat",
    moderationIterations,
    async () =>
    {
        for (int index = 0; index < moderationIterations; index++)
        {
            await pipeline.ProcessMessageAsync(Message(index, samples[index % samples.Length]));
        }
    }));

report.Results.Add(await MeasureAsync(
    "Moderation pipeline, one mention per chat",
    moderationIterations,
    async () =>
    {
        for (int index = 0; index < moderationIterations; index++)
        {
            await pipeline.ProcessMessageAsync(Message(
                index,
                $"@helpfulviewer {samples[index % samples.Length]}"));
        }
    }));
}

KokoroModelManager? kokoro = null;
if (!pacedOnly && !skipKokoro && File.Exists(Path.Combine(kokoroDirectory, "kokoro.onnx")))
{
    kokoro = new KokoroModelManager(kokoroDirectory, Path.Combine(kokoroDirectory, "voices"), speechThreads, noSpin);
    string[] voices = ["kokoro:af_heart", "kokoro:am_michael", "kokoro:bf_emma", "kokoro:bm_george"];
    const string speech = "Thanks for joining the stream. This is a SafeSpeak performance test.";

    if (!speechOnly) await SynthesizeAsync(kokoro, speech, voices[0]);

    if (speechOnly)
    {
        report.Notes.Add($"Speech process-only experiment: threads={speechThreads?.ToString() ?? "library default"}, disable spinning={noSpin}. Playback uses the real Windows audio router at zero volume. MiniLM is loaded but not running during speech-only scenarios.");
        using var windows = new SystemSpeechTtsEngine();
        using var router = new WasapiAudioRouter();
        foreach (bool neural in new[] { false, true })
        {
            try
            {
            using var stageTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Console.WriteLine($"Starting {(neural ? "Kokoro" : "Windows SAPI")} speech stage...");
            if (neural)
            {
                await using var warmup = new MemoryStream();
                await kokoro.SynthesizeAsync(speech, warmup, voices[0], 0, stageTimeout.Token);
            }
            else
            {
                await using var warmup = new MemoryStream();
                await windows.SynthesizeToWaveStreamAsync(speech, warmup, cancellationToken: stageTimeout.Token);
            }
            var waves = new List<byte[]>();
            double audioSeconds = 0;
            LoadResult synthesis = await MeasureAsync(neural ? "Kokoro synthesis" : "Windows SAPI synthesis", 4, async () =>
            {
                foreach (string voice in voices)
                {
                    await using var output = new MemoryStream();
                    if (neural) await kokoro.SynthesizeAsync(speech, output, voice, 0, stageTimeout.Token);
                    else await windows.SynthesizeToWaveStreamAsync(speech, output, cancellationToken: stageTimeout.Token);
                    byte[] wave = output.ToArray();
                    waves.Add(wave);
                    using var reader = new NAudio.Wave.WaveFileReader(new MemoryStream(wave));
                    audioSeconds += reader.TotalTime.TotalSeconds;
                }
            });
            synthesis.GeneratedAudioSeconds = audioSeconds;
            synthesis.RealTimeFactor = synthesis.ElapsedMilliseconds / 1000 / audioSeconds;
            report.Results.Add(synthesis);
            Console.WriteLine($"Synthesis completed: {synthesis.ElapsedMilliseconds:F0} ms, {synthesis.ProcessCpuPercent:F2}% process CPU.");
            if (!noPlayback)
            {
            report.Results.Add(await MeasureAsync(neural ? "Kokoro cached playback" : "Windows SAPI cached playback", 4, async () =>
            {
                foreach (byte[] wave in waves)
                {
                    using var stream = new MemoryStream(wave);
                    await router.PlayWaveStreamAsync(stream, volume: 0, cancellationToken: stageTimeout.Token);
                }
            }));
            }
            }
            catch (Exception exception)
            {
                string error = $"{(neural ? "Kokoro" : "Windows SAPI")} scenario failed: {exception.GetType().Name}: {exception.Message}";
                report.Notes.Add(error);
                Console.Error.WriteLine(error);
            }
        }
        if (queuePlayback)
        {
            try
            {
                Console.WriteLine("Starting real TTS queue with prefetch and paced moderation (muted)...");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var engine = new ModularTtsEngine(kokoro);
                await using var queue = new TtsQueue(engine, router)
                {
                    SelectedVoice = voices[0], SpeechVolume = 0, MaxQueueAgeSeconds = 0
                };
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                int completed = 0;
                queue.PlaybackFinished += (_, _) =>
                {
                    if (Interlocked.Increment(ref completed) == 4) finished.TrySetResult();
                };
                report.Results.Add(await MeasureAsync("Real Kokoro queue/prefetch + MiniLM at 1 message/sec", 4, async () =>
                {
                    queue.ArmAutomatic();
                    for (int index = 0; index < 4; index++)
                    {
                        if (!queue.Enqueue(new ModerationDecision { Message = Message(index, speech), SpokenText = speech }))
                            throw new InvalidOperationException("Test queue rejected an approved speech item.");
                    }
                    await Task.WhenAll(finished.Task.WaitAsync(timeout.Token), Task.Run(async () =>
                    {
                        Stopwatch schedule = Stopwatch.StartNew();
                        for (int index = 0; index < 25; index++)
                        {
                            double wait = index * 1000 - schedule.Elapsed.TotalMilliseconds;
                            if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), timeout.Token);
                            await pipeline.ProcessMessageAsync(Message(index + 9000, samples[index % samples.Length]), timeout.Token);
                        }
                        double remaining = 25_000 - schedule.Elapsed.TotalMilliseconds;
                        if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), timeout.Token);
                    }, timeout.Token));
                }));
            }
            catch (Exception exception)
            {
                report.Notes.Add($"Queue stage failed: {exception.GetType().Name}: {exception.Message}");
                Console.Error.WriteLine(report.Notes[^1]);
            }
        }
    }
    else
    {

    double sequentialAudioSeconds = 0;
    LoadResult sequentialKokoro = await MeasureAsync(
        "Kokoro, four voices sequential",
        voices.Length,
        async () =>
        {
            foreach (string voice in voices)
            {
                sequentialAudioSeconds += await SynthesizeAsync(kokoro, speech, voice);
            }
        });
    sequentialKokoro.GeneratedAudioSeconds = sequentialAudioSeconds;
    sequentialKokoro.RealTimeFactor =
        sequentialKokoro.ElapsedMilliseconds / 1000.0 / sequentialAudioSeconds;
    report.Results.Add(sequentialKokoro);

    double concurrentAudioSeconds = 0;
    LoadResult concurrentKokoro = await MeasureAsync(
        "Kokoro, four simultaneous requests",
        voices.Length,
        async () =>
        {
            double[] durations = await Task.WhenAll(
                voices.Select(voice => SynthesizeAsync(kokoro, speech, voice)));
            concurrentAudioSeconds = durations.Sum();
        });
    concurrentKokoro.GeneratedAudioSeconds = concurrentAudioSeconds;
    concurrentKokoro.RealTimeFactor =
        concurrentKokoro.ElapsedMilliseconds / 1000.0 / concurrentAudioSeconds;
    report.Results.Add(concurrentKokoro);

    report.Results.Add(await MeasureAsync(
        "Combined moderation and Kokoro",
        50,
        () => Task.WhenAll(
            Task.Run(async () =>
            {
                for (int index = 0; index < 50; index++)
                {
                    await pipeline.ProcessMessageAsync(Message(index + 1000, samples[index % samples.Length]));
                }
            }),
            Task.Run(async () =>
            {
                foreach (string voice in voices)
                {
                    await SynthesizeAsync(kokoro, speech, voice);
                }
            }))));
    }
}
else
{
    report.Notes.Add(skipKokoro
        ? "Kokoro scenarios were skipped by command-line option."
        : $"Kokoro model was not found in {kokoroDirectory}.");
}

kokoro?.Dispose();

string artifactDirectory = Path.Combine(
    Directory.GetCurrentDirectory(), "artifacts", "performance");
Directory.CreateDirectory(artifactDirectory);
string outputPath = Path.Combine(
    artifactDirectory,
    $"load-simulation-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
await File.WriteAllTextAsync(
    outputPath,
    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine();
foreach (LoadResult result in report.Results)
{
    string intentDetail = result.IntentInferenceOperations > 0
        ? $", intent calls {result.IntentInferenceOperations}"
        : "";
    string audioDetail = result.RealTimeFactor.HasValue
        ? $", audio {result.GeneratedAudioSeconds:F1}s, RTF {result.RealTimeFactor:F2}"
        : "";
    Console.WriteLine(
        $"{result.Name}: {result.ElapsedMilliseconds:F0} ms total, " +
        $"{result.ProcessCpuPercent:F2}% process CPU, {result.CpuTimeMilliseconds:F0} ms CPU time, " +
        $"processing average {result.AverageProcessingMilliseconds:F2} ms, p95 {result.P95ProcessingMilliseconds:F2} ms, " +
        $"peak working set {result.PeakWorkingSetMb:F1} MB{intentDetail}{audioDetail}");
}
Console.WriteLine($"Report: {outputPath}");
return 0;

static ChatMessage Message(int index, string text) => new()
{
    Author = $"viewer_{index}",
    AuthorDisplayName = $"Viewer {index}",
    RawText = text,
    Platform = "Synthetic",
    TimestampUtc = DateTimeOffset.UtcNow,
    EventType = LivestreamEventType.Chat
};

static async Task<double> SynthesizeAsync(KokoroModelManager manager, string text, string voice)
{
    await using var output = new MemoryStream();
    await manager.SynthesizeAsync(text, output, voice, rate: 0, CancellationToken.None);
    if (output.Length <= 44)
    {
        throw new InvalidDataException($"{voice} returned an empty wave file.");
    }
    return (output.Length - 44) / 48_000.0;
}

static async Task<LoadResult> MeasureAsync(
    string name,
    int operationCount,
    Func<Task> operation)
{
    using Process process = Process.GetCurrentProcess();
    process.Refresh();
    TimeSpan cpuBefore = process.TotalProcessorTime;
    long workingSetBefore = process.WorkingSet64;
    long peakWorkingSet = workingSetBefore;
    int gen0Before = GC.CollectionCount(0);
    int gen1Before = GC.CollectionCount(1);
    int gen2Before = GC.CollectionCount(2);
    long intentCountBefore = GetIntentInferenceCount();
    using var samplingCancellation = new CancellationTokenSource();
    Task sampler = Task.Run(async () =>
    {
        using Process sampledProcess = Process.GetCurrentProcess();
        while (!samplingCancellation.IsCancellationRequested)
        {
            try
            {
                sampledProcess.Refresh();
                peakWorkingSet = Math.Max(peakWorkingSet, sampledProcess.WorkingSet64);
                await Task.Delay(100, samplingCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    });

    Stopwatch stopwatch = Stopwatch.StartNew();
    try
    {
        await operation();
    }
    finally
    {
        stopwatch.Stop();
        samplingCancellation.Cancel();
        await sampler;
    }
    process.Refresh();
    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
    TimeSpan cpuUsed = process.TotalProcessorTime - cpuBefore;
    long intentCountAfter = GetIntentInferenceCount();
    double cpuPercent = stopwatch.Elapsed.TotalMilliseconds <= 0 || Environment.ProcessorCount <= 0
        ? 0
        : cpuUsed.TotalMilliseconds /
          (stopwatch.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100.0;

    return new LoadResult
    {
        Name = name,
        OperationCount = operationCount,
        ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
        MillisecondsPerOperation = operationCount > 0 ? stopwatch.Elapsed.TotalMilliseconds / operationCount : 0,
        ProcessCpuPercent = Math.Clamp(cpuPercent, 0, 100),
        CpuTimeMilliseconds = cpuUsed.TotalMilliseconds,
        WorkingSetBeforeMb = workingSetBefore / 1024.0 / 1024.0,
        WorkingSetAfterMb = process.WorkingSet64 / 1024.0 / 1024.0,
        PeakWorkingSetMb = peakWorkingSet / 1024.0 / 1024.0,
        Gen0Collections = GC.CollectionCount(0) - gen0Before,
        Gen1Collections = GC.CollectionCount(1) - gen1Before,
        Gen2Collections = GC.CollectionCount(2) - gen2Before,
        IntentInferenceOperations = intentCountAfter - intentCountBefore
    };
}

static long GetIntentInferenceCount() =>
    SubsystemPerformanceMetrics.Capture()
        .FirstOrDefault(metric => metric.Name == "IntentInference")?.OperationCount ?? 0;

internal sealed class LoadReport
{
    public DateTimeOffset TimestampUtc { get; init; }
    public MachineInfo Machine { get; init; } = new();
    public List<LoadResult> Results { get; } = [];
    public List<string> Notes { get; } = [];
}

internal sealed class MachineInfo
{
    public string OperatingSystem { get; init; } = "";
    public int ProcessorCount { get; init; }
    public string Framework { get; init; } = "";
    public string Architecture { get; init; } = "";
}

internal sealed class LoadResult
{
    public string Name { get; init; } = "";
    public int OperationCount { get; init; }
    public double ElapsedMilliseconds { get; init; }
    public double MillisecondsPerOperation { get; init; }
    public double ProcessCpuPercent { get; init; }
    public double CpuTimeMilliseconds { get; init; }
    public double WorkingSetBeforeMb { get; init; }
    public double WorkingSetAfterMb { get; init; }
    public double PeakWorkingSetMb { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    public long IntentInferenceOperations { get; init; }
    public double? GeneratedAudioSeconds { get; set; }
    public double? RealTimeFactor { get; set; }
    public double? AverageProcessingMilliseconds { get; set; }
    public double? P95ProcessingMilliseconds { get; set; }
}
