namespace SafeSpeak.Core.Accessibility;

/// <summary>
/// Private output for previewing a selected stream TTS voice without sending
/// the preview to the broadcast route.
/// </summary>
public interface IPrivateVoiceOutput
{
    void Speak(string text, bool interrupt = false);
    void Stop();
}
