namespace SafeSpeak.Core.Audio;

public enum TtsComputeTier
{
    All = 0,
    Level1_System = 1,
    Level2_Natural = 2,
    Level3_Kokoro = 3,
    Level4_Custom = 4
}

public sealed record VoiceInfo(
    string Id,
    string DisplayName,
    string Provider,
    string Culture,
    string Gender,
    string Description,
    bool IsNaturalNeural,
    int ComputeLevel = 3
)
{
    public string ComputeTierBadge => ComputeLevel switch
    {
        1 => "LVL 1 — System SAPI",
        2 => "LVL 2 — Windows Natural",
        3 => "LVL 3 — Kokoro Neural",
        4 => "LVL 4 — Custom Voice Pack",
        _ => $"LVL {ComputeLevel}"
    };

    public string ComputeTierDescription => ComputeLevel switch
    {
        1 => "Ultra-low compute (<1% CPU, 0% GPU). Legacy Windows Desktop SAPI speech fallback.",
        2 => "Lightweight neural. OS-managed low latency Windows Natural OneCore speech.",
        3 => "Standard neural. 27 high-fidelity offline voices on local CPU.",
        4 => "Custom cloned/imported offline voice package (Piper/ONNX/VITS) (Upcoming).",
        _ => "Standard speech synthesis."
    };
}

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
