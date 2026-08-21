namespace SafeSpeak.Core.Accessibility;

/// <summary>
/// Optional speech output used by SafeSpeak's integrated reader. Implementations
/// must keep this audio separate from the broadcast output.
/// </summary>
public interface IReaderSpeechOutput
{
    void Speak(string text, bool interrupt = false);
    bool TrySpeakCached(string text, bool interrupt = false);
    void WarmCache(string text, bool interrupt = false, bool persist = false);
    void Stop();
}
