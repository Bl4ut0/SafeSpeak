namespace SafeSpeak.Core.Audio;

public sealed record AudioEndpointInfo(string Id, string Name, bool IsDefault, bool IsVirtualCable);

/// <summary>
/// Routes audio stream to selected Windows WASAPI endpoints.
/// </summary>
public interface IAudioRouter : IDisposable
{
    IReadOnlyList<AudioEndpointInfo> GetOutputEndpoints();
    void SelectEndpoint(string? endpointId);
    string? SelectedEndpointId { get; }
    Task PlayWaveStreamAsync(Stream waveStream, float volume = 1.0f, CancellationToken cancellationToken = default);
    void Stop();
}
