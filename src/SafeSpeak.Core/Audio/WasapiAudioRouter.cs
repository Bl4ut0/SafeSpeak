using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SafeSpeak.Core.Logging;

namespace SafeSpeak.Core.Audio;

/// <summary>
/// Routes audio stream to selected Windows WASAPI endpoints.
/// </summary>
public sealed class WasapiAudioRouter : IAudioRouter
{
    private static readonly TimeSpan PlaybackLeadIn = TimeSpan.FromMilliseconds(100);
    private string? _selectedEndpointId;
    private WasapiOut? _wasapiOut;
    private MMDevice? _currentDevice;
    private WaveFileReader? _currentFileReader;
    private WaveChannel32? _currentVolumeProvider;
    private IWaveProvider? _currentPlaybackProvider;
    private readonly object _lock = new();
    private readonly MMDeviceEnumerator? _deviceEnumerator;
    private readonly AudioEndpointNotificationClient? _notificationClient;
    private Timer? _debounceTimer;

    public event EventHandler? EndpointsChanged;

    public string? SelectedEndpointId => _selectedEndpointId;

    public WasapiAudioRouter()
    {
        try
        {
            _deviceEnumerator = new MMDeviceEnumerator();
            _notificationClient = new AudioEndpointNotificationClient(OnNotificationChanged);
            _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("WasapiAudioRouter", $"Could not register audio endpoint notification callback: {ex.Message}");
        }
    }

    private void OnNotificationChanged()
    {
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                try
                {
                    EndpointsChanged?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning("WasapiAudioRouter", $"Error invoking EndpointsChanged: {ex.Message}");
                }
            }, null, 250, Timeout.Infinite);
        }
    }

    public IReadOnlyList<AudioEndpointInfo> GetOutputEndpoints()
    {
        var list = new List<AudioEndpointInfo>();

        try
        {
            MMDeviceEnumerator enumerator = _deviceEnumerator ?? new MMDeviceEnumerator();
            bool disposeEnumerator = _deviceEnumerator is null;
            try
            {
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
            finally
            {
                if (disposeEnumerator) enumerator.Dispose();
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
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(() =>
        {
            CancellationTokenRegistration cancellationRegistration = default;
            lock (_lock)
            {
                StopInternal();

                try
                {
                    waveStream.Seek(0, SeekOrigin.Begin);
                    _currentFileReader = new WaveFileReader(waveStream);
                    _currentVolumeProvider = new WaveChannel32(_currentFileReader)
                    {
                        Volume = Math.Clamp(volume, 0f, 2.5f),
                        PadWithZeroes = false
                    };
                    var delayedSamples = new OffsetSampleProvider(
                        new WaveToSampleProvider(_currentVolumeProvider))
                    {
                        // Some physical and virtual endpoints suppress the first
                        // syllable while their stream wakes. A short silent lead-in
                        // lets the endpoint settle before the viewer name begins.
                        DelayBy = PlaybackLeadIn
                    };

                    MMDevice? targetDevice = null;
                    MMDeviceEnumerator enumerator = _deviceEnumerator ?? new MMDeviceEnumerator();
                    bool disposeEnumerator = _deviceEnumerator is null;
                    try
                    {
                        if (!string.IsNullOrEmpty(_selectedEndpointId) && _selectedEndpointId != "default")
                        {
                            try { targetDevice = enumerator.GetDevice(_selectedEndpointId); } catch { }
                        }

                        targetDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    }
                    finally
                    {
                        if (disposeEnumerator) enumerator.Dispose();
                    }

                    _currentDevice = targetDevice;

                    ISampleProvider playbackSampleProvider = delayedSamples;
                    try
                    {
                        int targetSampleRate = targetDevice?.AudioClient?.MixFormat?.SampleRate ?? 0;
                        if (targetSampleRate > 0 && targetSampleRate != delayedSamples.WaveFormat.SampleRate)
                        {
                            playbackSampleProvider = new WdlResamplingSampleProvider(delayedSamples, targetSampleRate);
                        }
                    }
                    catch
                    {
                        playbackSampleProvider = delayedSamples;
                    }
                    _currentPlaybackProvider = new SampleToWaveProvider(playbackSampleProvider);

                    // A 100ms shared-mode buffer protects against buffer underruns
                    // and audio stuttering/glitching during CPU spikes from neural inference.
                    var output = new WasapiOut(targetDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
                    _wasapiOut = output;
                    output.Init(_currentPlaybackProvider);

                    void OnPlaybackStopped(object? sender, StoppedEventArgs e)
                    {
                        output.PlaybackStopped -= OnPlaybackStopped;
                        cancellationRegistration.Unregister();
                        if (e.Exception is not null)
                        {
                            tcs.TrySetException(e.Exception);
                        }
                        else
                        {
                            tcs.TrySetResult(true);
                        }
                    }

                    output.PlaybackStopped += OnPlaybackStopped;

                    if (cancellationToken.CanBeCanceled)
                    {
                        cancellationRegistration = cancellationToken.Register(() =>
                        {
                            Stop();
                            tcs.TrySetCanceled(cancellationToken);
                        });
                    }

                    output.Play();
                }
                catch (Exception ex)
                {
                    cancellationRegistration.Unregister();
                    tcs.TrySetException(ex);
                }
            }
        });

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

            _currentDevice?.Dispose();
            _currentDevice = null;

            _currentVolumeProvider?.Dispose();
            _currentVolumeProvider = null;
            _currentPlaybackProvider = null;
            _currentFileReader?.Dispose();
            _currentFileReader = null;
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            if (_deviceEnumerator is not null && _notificationClient is not null)
            {
                try
                {
                    _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationClient);
                }
                catch { }
            }
            _deviceEnumerator?.Dispose();
        }
    }

    internal sealed class AudioEndpointNotificationClient : IMMNotificationClient
    {
        private readonly Action _onChanged;

        public AudioEndpointNotificationClient(Action onChanged)
        {
            _onChanged = onChanged;
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _onChanged();
        public void OnDeviceAdded(string pwstrDeviceId) => _onChanged();
        public void OnDeviceRemoved(string deviceId) => _onChanged();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow is DataFlow.Render or DataFlow.All)
            {
                _onChanged();
            }
        }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => _onChanged();
    }
}
