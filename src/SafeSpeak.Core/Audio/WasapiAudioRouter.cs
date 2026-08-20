using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SafeSpeak.Core.Audio;

/// <summary>
/// Routes audio stream to selected Windows WASAPI endpoints.
/// </summary>
public sealed class WasapiAudioRouter : IAudioRouter
{
    private string? _selectedEndpointId;
    private WasapiOut? _wasapiOut;
    private WaveFileReader? _currentFileReader;
    private readonly object _lock = new();

    public string? SelectedEndpointId => _selectedEndpointId;

    public IReadOnlyList<AudioEndpointInfo> GetOutputEndpoints()
    {
        var list = new List<AudioEndpointInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            MMDevice? defaultDevice = null;
            try { defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); } catch { }

            foreach (var dev in devices)
            {
                bool isDefault = defaultDevice != null && dev.ID == defaultDevice.ID;
                bool isVirtual = dev.FriendlyName.Contains("Cable", StringComparison.OrdinalIgnoreCase) ||
                                 dev.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
                                 dev.FriendlyName.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

                list.Add(new AudioEndpointInfo(dev.ID, dev.FriendlyName, isDefault, isVirtual));
            }
        }
        catch
        {
            list.Add(new AudioEndpointInfo("default", "Windows Default Audio Device", true, false));
        }

        return list;
    }

    public void SelectEndpoint(string? endpointId)
    {
        lock (_lock)
        {
            _selectedEndpointId = endpointId;
        }
    }

    public Task PlayWaveStreamAsync(Stream waveStream, float volume = 1.0f, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<bool>();

        Task.Run(() =>
        {
            lock (_lock)
            {
                StopInternal();

                try
                {
                    waveStream.Seek(0, SeekOrigin.Begin);
                    _currentFileReader = new WaveFileReader(waveStream);

                    MMDevice? targetDevice = null;
                    using var enumerator = new MMDeviceEnumerator();

                    if (!string.IsNullOrEmpty(_selectedEndpointId) && _selectedEndpointId != "default")
                    {
                        try { targetDevice = enumerator.GetDevice(_selectedEndpointId); } catch { }
                    }

                    targetDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                    _wasapiOut = new WasapiOut(targetDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 80);
                    _wasapiOut.Init(_currentFileReader);

                    void OnPlaybackStopped(object? sender, StoppedEventArgs e)
                    {
                        if (_wasapiOut != null) _wasapiOut.PlaybackStopped -= OnPlaybackStopped;
                        tcs.TrySetResult(true);
                    }

                    _wasapiOut.PlaybackStopped += OnPlaybackStopped;

                    if (cancellationToken.CanBeCanceled)
                    {
                        cancellationToken.Register(() =>
                        {
                            Stop();
                            tcs.TrySetCanceled();
                        });
                    }

                    _wasapiOut.Play();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
        }, cancellationToken);

        return tcs.Task;
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        try
        {
            _wasapiOut?.Stop();
            _wasapiOut?.Dispose();
            _wasapiOut = null;

            _currentFileReader?.Dispose();
            _currentFileReader = null;
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
    }
}
