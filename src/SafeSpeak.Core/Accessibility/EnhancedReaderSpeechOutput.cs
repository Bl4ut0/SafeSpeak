using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using SafeSpeak.Core.Audio;

namespace SafeSpeak.Core.Accessibility;

/// <summary>
/// Synthesizes integrated-reader speech with the active SafeSpeak voice and
/// plays it through a dedicated private audio router. Frequently revisited UI
/// phrases are cached so neural inference never sits on the keyboard-focus path.
/// </summary>
public sealed class EnhancedReaderSpeechOutput : IReaderSpeechOutput, IAsyncDisposable
{
    private const int MemoryCacheEntryLimit = 128;
    private const long DiskCacheSizeLimit = 64L * 1024 * 1024;

    private sealed record SpeechRequest(
        long Version,
        string Text,
        string CacheKey,
        string? VoiceId,
        int Rate,
        int Volume,
        bool PlayWhenReady,
        bool Persist);

    private readonly ITtsEngine _ttsEngine;
    private readonly IAudioRouter _audioRouter;
    private readonly Channel<SpeechRequest> _requests;
    private readonly ConcurrentDictionary<string, byte[]> _memoryCache = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _playbackLock = new();
    private readonly string _cacheDirectory;
    private readonly Task _worker;
    private readonly Task _cacheCleanupTask;
    private CancellationTokenSource? _playbackCts;
    private long _latestVersion;
    private bool _disposed;

    public string? VoiceId { get; set; }
    public int Rate { get; set; }
    public int Volume { get; set; } = 100;

    public EnhancedReaderSpeechOutput(
        ITtsEngine ttsEngine,
        IAudioRouter audioRouter,
        string? cacheDirectory = null)
    {
        _ttsEngine = ttsEngine;
        _audioRouter = audioRouter;
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSpeak",
            "Cache",
            "ReaderSpeech");

        _requests = Channel.CreateBounded<SpeechRequest>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _worker = Task.Run(ProcessRequestsAsync);
        _cacheCleanupTask = Task.Run(CleanupDiskCache);
    }

    public void Speak(string text, bool interrupt = false)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text)) return;
        if (TrySpeakCached(text, interrupt)) return;
        QueueSynthesis(text, interrupt, playWhenReady: true, persist: false);
    }

    public bool TrySpeakCached(string text, bool interrupt = false)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text)) return false;

        int rate = Math.Clamp(Rate, -5, 5);
        string cacheKey = CreateCacheKey(text, VoiceId, rate);
        if (!TryReadCache(cacheKey, out byte[] waveBytes)) return false;

        long version = Interlocked.Increment(ref _latestVersion);
        if (interrupt) StopPlayback();
        _ = PlayBytesAsync(waveBytes, Math.Clamp(Volume, 0, 100), version);
        return true;
    }

    public void WarmCache(string text, bool interrupt = false, bool persist = false)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text)) return;

        int rate = Math.Clamp(Rate, -5, 5);
        string cacheKey = CreateCacheKey(text, VoiceId, rate);
        if (TryReadCache(cacheKey, out _)) return;
        QueueSynthesis(text, interrupt, playWhenReady: false, persist);
    }

    public void Stop()
    {
        Interlocked.Increment(ref _latestVersion);
        while (_requests.Reader.TryRead(out _)) { }
        StopPlayback();
    }

    private void QueueSynthesis(string text, bool interrupt, bool playWhenReady, bool persist)
    {
        int rate = Math.Clamp(Rate, -5, 5);
        string? voiceId = VoiceId;
        long version = Interlocked.Increment(ref _latestVersion);
        if (interrupt) StopPlayback();

        _requests.Writer.TryWrite(new SpeechRequest(
            version,
            text,
            CreateCacheKey(text, voiceId, rate),
            voiceId,
            rate,
            Math.Clamp(Volume, 0, 100),
            playWhenReady,
            persist));
    }

    private async Task ProcessRequestsAsync()
    {
        try
        {
            await foreach (SpeechRequest initialRequest in _requests.Reader.ReadAllAsync(_lifetimeCts.Token))
            {
                SpeechRequest request = initialRequest;
                while (_requests.Reader.TryRead(out SpeechRequest? newerRequest)) request = newerRequest;

                try
                {
                    if (!TryReadCache(request.CacheKey, out byte[] waveBytes))
                    {
                        using var waveStream = new MemoryStream();
                        await _ttsEngine.SynthesizeToWaveStreamAsync(
                            request.Text,
                            waveStream,
                            request.VoiceId,
                            request.Rate,
                            100,
                            _lifetimeCts.Token);
                        waveBytes = waveStream.ToArray();
                        StoreInMemory(request.CacheKey, waveBytes);
                        if (request.Persist) PersistCache(request.CacheKey, waveBytes);
                    }

                    if (request.PlayWhenReady && request.Version == Volatile.Read(ref _latestVersion))
                    {
                        await PlayBytesAsync(waveBytes, request.Volume, request.Version);
                    }
                }
                catch (OperationCanceledException) { }
                catch
                {
                    // Reader failures must never interrupt keyboard navigation or crash SafeSpeak.
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task PlayBytesAsync(byte[] waveBytes, int volume, long version)
    {
        if (version != Volatile.Read(ref _latestVersion) || _disposed) return;

        var playbackCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        lock (_playbackLock)
        {
            if (version != Volatile.Read(ref _latestVersion) || _disposed)
            {
                playbackCts.Dispose();
                return;
            }

            _playbackCts?.Cancel();
            _audioRouter.Stop();
            _playbackCts = playbackCts;
        }

        try
        {
            await _audioRouter.PlayWaveStreamAsync(
                new MemoryStream(waveBytes, writable: false),
                volume / 100.0f,
                playbackCts.Token);
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            lock (_playbackLock)
            {
                if (ReferenceEquals(_playbackCts, playbackCts)) _playbackCts = null;
            }
            playbackCts.Dispose();
        }
    }

    private bool TryReadCache(string cacheKey, out byte[] waveBytes)
    {
        if (_memoryCache.TryGetValue(cacheKey, out byte[]? cachedBytes) && cachedBytes is not null)
        {
            waveBytes = cachedBytes;
            return true;
        }

        try
        {
            string path = GetCachePath(cacheKey);
            if (!File.Exists(path))
            {
                waveBytes = Array.Empty<byte>();
                return false;
            }

            waveBytes = File.ReadAllBytes(path);
            if (waveBytes.Length < 44)
            {
                waveBytes = Array.Empty<byte>();
                return false;
            }

            StoreInMemory(cacheKey, waveBytes);
            return true;
        }
        catch
        {
            waveBytes = Array.Empty<byte>();
            return false;
        }
    }

    private void StoreInMemory(string cacheKey, byte[] waveBytes)
    {
        if (_memoryCache.Count >= MemoryCacheEntryLimit) _memoryCache.Clear();
        _memoryCache[cacheKey] = waveBytes;
    }

    private void PersistCache(string cacheKey, byte[] waveBytes)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            string path = GetCachePath(cacheKey);
            string temporaryPath = path + ".tmp";
            File.WriteAllBytes(temporaryPath, waveBytes);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch { }
    }

    private void CleanupDiskCache()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory)) return;

            long retainedBytes = 0;
            foreach (FileInfo file in new DirectoryInfo(_cacheDirectory)
                         .EnumerateFiles("*.wav")
                         .OrderByDescending(file => file.LastWriteTimeUtc))
            {
                retainedBytes += file.Length;
                if (retainedBytes > DiskCacheSizeLimit) file.Delete();
            }
        }
        catch { }
    }

    private string GetCachePath(string cacheKey) => Path.Combine(_cacheDirectory, cacheKey + ".wav");

    private static string CreateCacheKey(string text, string? voiceId, int rate)
    {
        string material = $"v2\n{voiceId ?? "default"}\n{rate}\n{text}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private void StopPlayback()
    {
        lock (_playbackLock)
        {
            _playbackCts?.Cancel();
            _audioRouter.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;
        _requests.Writer.TryComplete();
        _lifetimeCts.Cancel();
        StopPlayback();

        try { await _worker; }
        catch (OperationCanceledException) { }
        try { await _cacheCleanupTask; }
        catch { }

        lock (_playbackLock)
        {
            _playbackCts?.Dispose();
            _playbackCts = null;
        }
        _lifetimeCts.Dispose();
    }
}
