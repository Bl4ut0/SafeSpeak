using SafeSpeak.Core.Audio;

namespace SafeSpeak.Mobile.Services;

/// <summary>Uses the operating system's installed voices; no stream connector is required.</summary>
public sealed class MobileSpeechOutput : ISpeechOutput
{
    private readonly object _sync = new();
    private CancellationTokenSource? _activeSpeech;
    private int _isSpeaking;
    private bool _disposed;

    public bool IsSpeaking => Volatile.Read(ref _isSpeaking) != 0;

    public async Task SpeakAsync(
        SpeechOutputRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);

        CancellationTokenSource speechCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync)
        {
            _activeSpeech?.Cancel();
            _activeSpeech?.Dispose();
            _activeSpeech = speechCancellation;
        }

        Interlocked.Exchange(ref _isSpeaking, 1);
        try
        {
            Locale? locale = await FindLocaleAsync(request.Culture, speechCancellation.Token);
            var options = new SpeechOptions
            {
                Locale = locale,
                Pitch = Math.Clamp(request.Pitch, 0.0f, 2.0f),
                Volume = Math.Clamp(request.Volume, 0.0f, 1.0f)
            };

            await TextToSpeech.Default.SpeakAsync(request.Text, options, speechCancellation.Token);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeSpeech, speechCancellation))
                {
                    _activeSpeech = null;
                    Interlocked.Exchange(ref _isSpeaking, 0);
                }
            }

            speechCancellation.Dispose();
        }
    }

    public Task StopAsync()
    {
        lock (_sync)
        {
            _activeSpeech?.Cancel();
        }

        Interlocked.Exchange(ref _isSpeaking, 0);
        return Task.CompletedTask;
    }

    private static async Task<Locale?> FindLocaleAsync(string? requestedCulture, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestedCulture)) return null;

        IEnumerable<Locale> locales = await TextToSpeech.Default.GetLocalesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return locales.FirstOrDefault(locale =>
            string.Equals(locale.Id, requestedCulture, StringComparison.OrdinalIgnoreCase) ||
            requestedCulture.StartsWith(locale.Language, StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        lock (_sync)
        {
            _activeSpeech?.Dispose();
            _activeSpeech = null;
        }
    }
}
