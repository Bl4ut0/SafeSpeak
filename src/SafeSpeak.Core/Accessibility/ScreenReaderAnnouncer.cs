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

        Task.Run(() =>
        {
            lock (_lock)
            {
                if (_privateSynth == null) return;

                if (interrupt)
                {
                    _privateSynth.SpeakAsyncCancelAll();
                }

                _privateSynth.SpeakAsync(text);
            }
        });
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
