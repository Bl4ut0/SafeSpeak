namespace SafeSpeak.Core.Accessibility;

public enum SoundCueType
{
    Armed,
    Disarmed,
    MessageApproved,
    MessageBlocked,
    TikFinityConnected,
    TikFinityDisconnected,
    EmergencyPanic,
    QueueEmpty
}

/// <summary>
/// Contract for private screen reader speech and audio cue announcements that never enter broadcast audio.
/// </summary>
public interface IScreenReaderBridge : IDisposable
{
    bool IsEnhancedAccessibilityEnabled { get; set; }
    void Announce(string text, bool interrupt = false);
    void PlayCue(SoundCueType cueType);
}
