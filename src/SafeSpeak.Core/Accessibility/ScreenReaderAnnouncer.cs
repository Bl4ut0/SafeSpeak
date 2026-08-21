using System.Speech.Synthesis;

namespace SafeSpeak.Core.Accessibility;

/// <summary>
/// Handles private auditory notifications and UIA screen reader announcements.
/// Audio is directed strictly to the user's default communication channel and never enters the broadcast audio session.
/// </summary>
public sealed class ScreenReaderAnnouncer : IScreenReaderBridge
{
    private SpeechSynthesizer? _privateSynth;
    private IReaderSpeechOutput? _enhancedSpeechOutput;
    private readonly object _lock = new();

    public bool IsEnhancedAccessibilityEnabled { get; set; } = true;

    public event EventHandler<string>? AnnouncementRequested;

    public void SetEnhancedSpeechOutput(IReaderSpeechOutput? speechOutput)
    {
        lock (_lock)
        {
            _enhancedSpeechOutput?.Stop();
            _enhancedSpeechOutput = speechOutput;
        }
    }

    public ScreenReaderAnnouncer()
    {
        try
        {
            _privateSynth = new SpeechSynthesizer();
            _privateSynth.Rate = 2; // Slightly faster for screen reader announcements
            _privateSynth.SetOutputToDefaultAudioDevice();
        }
        catch
        {
            _privateSynth = null;
        }
    }

    public void Announce(string text, bool interrupt = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Trigger UIA Live Region event for NVDA/JAWS/Narrator
        AnnouncementRequested?.Invoke(this, text);

        if (!IsEnhancedAccessibilityEnabled) return;

        lock (_lock)
        {
            if (_enhancedSpeechOutput is not null)
            {
                if (interrupt)
                {
                    try { _privateSynth?.SpeakAsyncCancelAll(); } catch { }
                }
                _enhancedSpeechOutput.Speak(text, interrupt);
                return;
            }

            SpeakWithSystemVoice(text, interrupt);
        }
    }

    /// <summary>
    /// Announces keyboard focus without waiting for first-use neural synthesis.
    /// Cached phrases use the selected enhanced voice immediately; cache misses
    /// use the low-latency Windows voice once while warming the enhanced phrase.
    /// </summary>
    public void AnnounceFocus(string text, bool persistCache)
    {
        if (!IsEnhancedAccessibilityEnabled || string.IsNullOrWhiteSpace(text)) return;

        lock (_lock)
        {
            if (_enhancedSpeechOutput is not null)
            {
                if (_enhancedSpeechOutput.TrySpeakCached(text, interrupt: true))
                {
                    try { _privateSynth?.SpeakAsyncCancelAll(); } catch { }
                    return;
                }
                _enhancedSpeechOutput.WarmCache(text, interrupt: true, persist: persistCache);
            }

            SpeakWithSystemVoice(text, interrupt: true);
        }
    }

    private void SpeakWithSystemVoice(string text, bool interrupt)
    {
        if (_privateSynth == null) return;

        try
        {
            if (interrupt)
            {
                _privateSynth.SpeakAsyncCancelAll();
            }

            _privateSynth.SpeakAsync(text);
        }
        catch (ObjectDisposedException)
        {
            // The application is closing; no announcement is required.
        }
        catch (InvalidOperationException)
        {
            // The Windows speech service is temporarily unavailable.
        }
    }

    public void PlayCue(SoundCueType cueType)
    {
        if (IsEnhancedAccessibilityEnabled)
        {
            SoundCuePlayer.PlayCue(cueType);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _enhancedSpeechOutput?.Stop();
            _enhancedSpeechOutput = null;
            _privateSynth?.Dispose();
            _privateSynth = null;
        }
    }
}
