using NAudio.CoreAudioApi;
using SafeSpeak.Core.Audio;

namespace SafeSpeak.Core.Tests;

public sealed class AudioEndpointNotificationTests
{
    [Fact]
    public void NotificationClient_OnDeviceAdded_InvokesCallback()
    {
        int callCount = 0;
        var client = new WasapiAudioRouter.AudioEndpointNotificationClient(() => callCount++);

        client.OnDeviceAdded("device_123");

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void NotificationClient_OnDeviceRemoved_InvokesCallback()
    {
        int callCount = 0;
        var client = new WasapiAudioRouter.AudioEndpointNotificationClient(() => callCount++);

        client.OnDeviceRemoved("device_123");

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void NotificationClient_OnDeviceStateChanged_InvokesCallback()
    {
        int callCount = 0;
        var client = new WasapiAudioRouter.AudioEndpointNotificationClient(() => callCount++);

        client.OnDeviceStateChanged("device_123", DeviceState.Active);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void NotificationClient_OnDefaultDeviceChanged_RenderFlow_InvokesCallback()
    {
        int callCount = 0;
        var client = new WasapiAudioRouter.AudioEndpointNotificationClient(() => callCount++);

        client.OnDefaultDeviceChanged(DataFlow.Render, Role.Multimedia, "device_123");

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void NotificationClient_OnDefaultDeviceChanged_CaptureFlow_DoesNotInvokeCallback()
    {
        int callCount = 0;
        var client = new WasapiAudioRouter.AudioEndpointNotificationClient(() => callCount++);

        client.OnDefaultDeviceChanged(DataFlow.Capture, Role.Multimedia, "device_123");

        Assert.Equal(0, callCount);
    }

    [Fact]
    public void NotificationClient_OnPropertyValueChanged_InvokesCallback()
    {
        int callCount = 0;
        var client = new WasapiAudioRouter.AudioEndpointNotificationClient(() => callCount++);

        client.OnPropertyValueChanged("device_123", default);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void WasapiAudioRouter_CanSubscribeAndUnsubscribeEndpointsChanged()
    {
        using var router = new WasapiAudioRouter();
        bool received = false;
        EventHandler handler = (_, _) => received = true;

        router.EndpointsChanged += handler;
        router.EndpointsChanged -= handler;

        Assert.False(received);
    }
}
