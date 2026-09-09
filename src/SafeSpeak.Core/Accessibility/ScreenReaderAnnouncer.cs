using SafeSpeak.Core.Audio;

namespace SafeSpeak.Core.Accessibility;

/// <summary>
/// Handles SafeSpeak's built-in spoken guidance. Speech uses the Windows default
/// playback device; stream software may capture it if desktop audio is included.
/// </summary>
public sealed class ScreenReaderAnnouncer : IScreenReaderBridge
{
    private readonly SystemSpeechTtsEngine? _speechEngine;
    private readonly IAudioRouter? _guidanceAudioRouter;
    private CancellationTokenSource _speechCancellation = new();
    private Task _speechTask = Task.CompletedTask;
    private int _speechRate = 2;
    private int _speechVolume = 100;
    private readonly object _lock = new();

    public bool IsEnhancedAccessibilityEnabled { get; set; } = true;

    public bool IsSpeechAvailable
    {
        get
        {
            lock (_lock) return _speechEngine is not null && _guidanceAudioRouter is not null;
        }
    }

    public event EventHandler<string>? AnnouncementRequested;

    public int SpeechRate
    {
        get
        {
            lock (_lock) return _speechRate;
        }
        set
        {
            lock (_lock)
            {
                _speechRate = Math.Clamp(value, -10, 10);
            }
        }
    }

    public int SpeechVolume
    {
        get
        {
            lock (_lock) return _speechVolume;
        }
        set
        {
            lock (_lock) _speechVolume = Math.Clamp(value, 0, 150);
        }
    }

    public string? SelectedAudioEndpointId => _guidanceAudioRouter?.SelectedEndpointId;

    public ScreenReaderAnnouncer()
    {
        try
        {
            _speechEngine = new SystemSpeechTtsEngine();
            _guidanceAudioRouter = new WasapiAudioRouter();
        }
        catch
        {
            _speechEngine = null;
            _guidanceAudioRouter = null;
        }
    }

    public IReadOnlyList<AudioEndpointInfo> GetOutputEndpoints() =>
        _guidanceAudioRouter?.GetOutputEndpoints() ?? [];

    public void SelectAudioEndpoint(string? endpointId) =>
        _guidanceAudioRouter?.SelectEndpoint(endpointId);

    public void Announce(string text, bool interrupt = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Non-WPF hosts may mirror this request. WPF views raise real
        // LiveRegionChanged events through the LiveRegion attached behavior.
        AnnouncementRequested?.Invoke(this, text);

        if (!IsEnhancedAccessibilityEnabled) return;

        lock (_lock)
        {
            SpeakWithSystemVoice(text, interrupt);
        }
    }

    /// <summary>
    /// Speaks information the user explicitly requested, even when automatic
    /// built-in focus and state guidance is disabled.
    /// </summary>
    public void AnnounceOnDemand(string text, bool interrupt = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        AnnouncementRequested?.Invoke(this, text);
        lock (_lock)
        {
            SpeakWithSystemVoice(text, interrupt);
        }
    }

    /// <summary>
    /// Announces keyboard focus without waiting for first-use neural synthesis.
    /// Focus always uses the low-latency Windows voice. Neural stream voices are
    /// deliberately kept out of keyboard navigation to avoid delay and overlap.
    /// </summary>
    public void AnnounceFocus(string text)
    {
        if (!IsEnhancedAccessibilityEnabled || string.IsNullOrWhiteSpace(text)) return;

        lock (_lock)
        {
            SpeakWithSystemVoice(text, interrupt: true);
        }
    }

    private void SpeakWithSystemVoice(string text, bool interrupt)
    {
        if (_speechEngine is null || _guidanceAudioRouter is null) return;

        if (interrupt)
        {
            CancelSpeechUnsafe();
            _speechCancellation.Dispose();
            _speechCancellation = new CancellationTokenSource();
            _speechTask = Task.CompletedTask;
        }

        Task preceding = _speechTask;
        CancellationToken token = _speechCancellation.Token;
        int rate = _speechRate;
        int volume = _speechVolume;
        _speechTask = SpeakAfterAsync(preceding, text, rate, volume, token);
    }

    private async Task SpeakAfterAsync(
        Task preceding,
        string text,
        int rate,
        int volume,
        CancellationToken cancellationToken)
    {
        try
        {
            try { await preceding.ConfigureAwait(false); }
            catch { }
            cancellationToken.ThrowIfCancellationRequested();
            using var waveStream = new MemoryStream();
            await _speechEngine!.SynthesizeToWaveStreamAsync(
                text,
                waveStream,
                rate: rate,
                volume: 100,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await _guidanceAudioRouter!.PlayWaveStreamAsync(
                waveStream,
                volume / 100f,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Stops only SafeSpeak's built-in interface guidance. Livestream text to
    /// speech is owned by TtsQueue and is deliberately unaffected.
    /// </summary>
    public void StopSpeaking()
    {
        lock (_lock)
        {
            CancelSpeechUnsafe();
            _speechCancellation.Dispose();
            _speechCancellation = new CancellationTokenSource();
            _speechTask = Task.CompletedTask;
        }
    }

    public void PlayCue(SoundCueType cueType)
    {
        if (IsEnhancedAccessibilityEnabled)
        {
            lock (_lock)
            {
                if (_guidanceAudioRouter is null) return;
                Task preceding = _speechTask;
                CancellationToken token = _speechCancellation.Token;
                float volume = _speechVolume / 100f;
                _speechTask = PlayCueAfterAsync(preceding, cueType, volume, token);
            }
        }
    }

    private async Task PlayCueAfterAsync(
        Task preceding,
        SoundCueType cueType,
        float volume,
        CancellationToken cancellationToken)
    {
        try
        {
            try { await preceding.ConfigureAwait(false); }
            catch { }
            cancellationToken.ThrowIfCancellationRequested();
            await SoundCuePlayer.PlayCueAsync(
                cueType,
                _guidanceAudioRouter!,
                volume,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        catch (IOException) { }
    }

    private void CancelSpeechUnsafe()
    {
        try { _speechCancellation.Cancel(); } catch (ObjectDisposedException) { }
        try { _speechEngine?.Stop(); } catch (ObjectDisposedException) { }
        try { _guidanceAudioRouter?.Stop(); } catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            CancelSpeechUnsafe();
            _speechCancellation.Dispose();
            _speechEngine?.Dispose();
            _guidanceAudioRouter?.Dispose();
        }
    }
}
