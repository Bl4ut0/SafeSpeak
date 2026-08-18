using System.Speech.Synthesis;

namespace SafeSpeak.App.Accessibility;

public sealed class WindowsSpokenGuidanceService : ISpokenGuidanceService
{
    private readonly object _sync = new();
    private SpeechSynthesizer? _synthesizer;

    public WindowsSpokenGuidanceService()
    {
        try
        {
            _synthesizer = new SpeechSynthesizer();
            _synthesizer.SetOutputToDefaultAudioDevice();
        }
        catch
        {
            _synthesizer?.Dispose();
            _synthesizer = null;
        }
    }

    public bool IsAvailable => _synthesizer is not null;

    public void Speak(string text, bool interrupt = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_sync)
        {
            if (_synthesizer is null)
            {
                return;
            }

            try
            {
                if (interrupt)
                {
                    _synthesizer.SpeakAsyncCancelAll();
                }

                _synthesizer.SpeakAsync(text);
            }
            catch
            {
                // Native screen readers and the visible UI remain available if Windows speech fails.
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            try
            {
                _synthesizer?.SpeakAsyncCancelAll();
            }
            catch
            {
                // Speech cancellation is best effort during shutdown or device changes.
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_synthesizer is null)
            {
                return;
            }

            try
            {
                _synthesizer.SpeakAsyncCancelAll();
            }
            catch
            {
                // Continue disposing the synthesizer even if cancellation fails.
            }

            _synthesizer.Dispose();
            _synthesizer = null;
        }
    }
}
