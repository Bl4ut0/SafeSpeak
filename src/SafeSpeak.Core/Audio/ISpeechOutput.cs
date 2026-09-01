namespace SafeSpeak.Core.Audio;

/// <summary>
/// A platform-neutral request for immediate speech output. Mobile implementations
/// can use Android or iOS system voices; desktop synthesis remains behind ITtsEngine.
/// </summary>
public sealed record SpeechOutputRequest(
    string Text,
    string? Culture = null,
    float Pitch = 1.0f,
    float Volume = 1.0f);

/// <summary>
/// Minimal speech contract for platform-native TTS. It deliberately has no
/// dependency on TikFinity or any livestream provider.
/// </summary>
public interface ISpeechOutput : IAsyncDisposable
{
    bool IsSpeaking { get; }
    Task SpeakAsync(SpeechOutputRequest request, CancellationToken cancellationToken = default);
    Task StopAsync();
}
