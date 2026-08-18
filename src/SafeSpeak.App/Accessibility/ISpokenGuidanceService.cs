namespace SafeSpeak.App.Accessibility;

public interface ISpokenGuidanceService : IDisposable
{
    bool IsAvailable { get; }

    void Speak(string text, bool interrupt = true);

    void Stop();
}
