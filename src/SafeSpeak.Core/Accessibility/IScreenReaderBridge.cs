namespace SafeSpeak.Core.Accessibility;

public enum SoundCueType
{
    Armed,
    Disarmed,
    MessageApproved,
    MessageBlocked,
    TikFinityConnected,
    TikFinityDisconnected,
    EmergencyStop,
    QueueEmpty
}

/// <summary>
/// Contract for SafeSpeak's optional built-in spoken guidance and audio cues.
/// Routing depends on the implementation and must be described accurately in the UI.
/// </summary>
public interface IScreenReaderBridge : IDisposable
{
    bool IsEnhancedAccessibilityEnabled { get; set; }
    void Announce(string text, bool interrupt = false);
    void PlayCue(SoundCueType cueType);
}
