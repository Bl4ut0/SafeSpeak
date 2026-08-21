using SafeSpeak.Core.Audio;

namespace SafeSpeak.Core.Tests;

public sealed class AudioEndpointFormatterTests
{
    private static readonly AudioEndpointInfo[] Endpoints =
    [
        new("{0.0.0.00000000}.long-numeric-system-id", "Speakers (Realtek Audio)", true, false),
        new("{0.0.0.00000000}.virtual-cable-id", "CABLE Input (VB-Audio Virtual Cable)", false, true)
    ];

    [Fact]
    public void GetFriendlyName_KnownEndpoint_ReturnsDeviceName()
    {
        string result = AudioEndpointFormatter.GetFriendlyName(
            Endpoints,
            "{0.0.0.00000000}.virtual-cable-id");

        Assert.Equal("CABLE Input (VB-Audio Virtual Cable)", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("default")]
    public void GetFriendlyName_DefaultSelection_ReturnsDefaultDeviceName(string? endpointId)
    {
        string result = AudioEndpointFormatter.GetFriendlyName(Endpoints, endpointId);

        Assert.Equal("Speakers (Realtek Audio)", result);
    }

    [Fact]
    public void GetFriendlyName_StaleEndpoint_DoesNotExposeRawIdentifier()
    {
        const string staleId = "{0.0.0.00000000}.123456789";

        string result = AudioEndpointFormatter.GetFriendlyName(Endpoints, staleId);

        Assert.Equal(AudioEndpointFormatter.UnavailableDevice, result);
        Assert.DoesNotContain(staleId, result, StringComparison.OrdinalIgnoreCase);
    }
}
