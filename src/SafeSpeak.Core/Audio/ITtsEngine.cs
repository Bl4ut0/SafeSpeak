namespace SafeSpeak.Core.Audio;

public sealed record VoiceInfo(
    string Id,
    string DisplayName,
    string Provider,
    string Culture,
    string Gender,
    string Description,
    bool IsNaturalNeural
);

/// <summary>
/// Contract for modular TTS synthesis engines.
/// </summary>
public interface ITtsEngine : IDisposable
{
    IReadOnlyList<VoiceInfo> GetAvailableVoices();
    Task SynthesizeToWaveStreamAsync(string text, Stream outputStream, string? voiceId = null, int rate = 0, int volume = 100, CancellationToken cancellationToken = default);
    Task SpeakDirectAsync(string text, string? voiceId = null, int rate = 0, int volume = 100, CancellationToken cancellationToken = default);
    void Stop();
}
