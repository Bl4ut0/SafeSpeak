namespace SafeSpeak.Core.Audio;

/// <summary>
/// Converts persistent Windows audio endpoint identifiers into safe, human-readable labels.
/// Raw endpoint identifiers must never be spoken because they can contain long numeric values.
/// </summary>
public static class AudioEndpointFormatter
{
    public const string WindowsDefaultDevice = "Windows default audio device";
    public const string UnavailableDevice = "Previously selected audio device is unavailable";

    public static string GetFriendlyName(
        IEnumerable<AudioEndpointInfo> endpoints,
        string? endpointId)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        AudioEndpointInfo[] availableEndpoints = endpoints as AudioEndpointInfo[] ?? endpoints.ToArray();

        if (string.IsNullOrWhiteSpace(endpointId) ||
            string.Equals(endpointId, "default", StringComparison.OrdinalIgnoreCase))
        {
            return availableEndpoints.FirstOrDefault(endpoint => endpoint.IsDefault)?.Name
                ?? WindowsDefaultDevice;
        }

        return availableEndpoints.FirstOrDefault(
                endpoint => string.Equals(endpoint.Id, endpointId, StringComparison.OrdinalIgnoreCase))?.Name
            ?? UnavailableDevice;
    }
}
