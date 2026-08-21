using System.Speech.Synthesis;

namespace SafeSpeak.Core.Accessibility;

/// <summary>
/// Handles private auditory notifications and UIA screen reader announcements.
/// Audio is directed strictly to the user's default communication channel and never enters the broadcast audio session.
/// </summary>
public sealed class ScreenReaderAnnouncer : IScreenReaderBridge
{
    private SpeechSynthesizer? _privateSynth;
    private readonly object _lock = new();

    public bool IsEnhancedAccessibilityEnabled { get; set; } = true;

    public event EventHandler<string>? AnnouncementRequested;

    public int SpeechRate
    {
        get
        {
            lock (_lock) return _privateSynth?.Rate ?? 2;
        }
        set
        {
            lock (_lock)
            {
                if (_privateSynth is not null) _privateSynth.Rate = Math.Clamp(value, -10, 10);
            }
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
            _privateSynth?.Dispose();
            _privateSynth = null;
        }
    }
}
