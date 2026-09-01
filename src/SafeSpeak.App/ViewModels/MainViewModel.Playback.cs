using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Models;

namespace SafeSpeak.App.ViewModels;

/// <summary>
/// WPF-facing playback state and commands. TtsQueue remains the only owner of
/// armed, playback-mode, queue, and current-speech transitions.
/// </summary>
public sealed partial class MainViewModel
{
    [ObservableProperty]
    private bool _isArmed;

    [ObservableProperty]
    private bool _isAutoPlay;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isSpeaking;

    [ObservableProperty]
    private int _queueCount;

    public string ArmedStatusText => IsArmed ? "Armed" : "Disarmed";

    public string PlaybackModeStatus => _ttsQueue.Mode switch
    {
        TtsPlaybackMode.Automatic => "Automatic",
        TtsPlaybackMode.Paused => "Paused",
        TtsPlaybackMode.Manual => "Manual",
        _ => "Disarmed"
    };

    public string PauseButtonText => IsPaused ? "_Resume TTS" : "_Pause TTS";

    public string PauseButtonAutomationName => IsPaused
        ? "Resume automatic text to speech button"
        : "Pause text to speech after the current message button";

    public bool ShowAutomaticOrPausedControls =>
        IsArmed && (IsAutoPlay || IsPaused);

    public bool ShowManualControls =>
        IsArmed && !IsAutoPlay && !IsPaused;

    partial void OnIsArmedChanged(bool value)
    {
        OnPropertyChanged(nameof(ArmedStatusText));
        NotifyPlaybackControlVisibilityChanged();
    }

    partial void OnIsAutoPlayChanged(bool value)
    {
        OnPropertyChanged(nameof(PlaybackModeStatus));
        NotifyPlaybackControlVisibilityChanged();
    }

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PlaybackModeStatus));
        OnPropertyChanged(nameof(PauseButtonText));
        OnPropertyChanged(nameof(PauseButtonAutomationName));
        NotifyPlaybackControlVisibilityChanged();
    }

    private void NotifyPlaybackControlVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowAutomaticOrPausedControls));
        OnPropertyChanged(nameof(ShowManualControls));
    }

    private void TtsQueue_StateChanged(
        object? sender,
        TtsQueueStateChangedEventArgs e)
    {
        if (_incomingEventCts.IsCancellationRequested) return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_incomingEventCts.IsCancellationRequested) return;
            IsArmed = e.IsArmed;
            IsAutoPlay = e.IsAutoPlay;
            IsPaused = e.IsPaused;
            IsSpeaking = e.IsSpeaking;
            QueueCount = e.QueueCount;
        });
    }

    private void TtsQueue_PlaybackStarted(
        object? sender,
        ModerationDecision decision)
    {
        if (_incomingEventCts.IsCancellationRequested) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!_incomingEventCts.IsCancellationRequested) IsSpeaking = true;
        });
    }

    private void TtsQueue_PlaybackFinished(
        object? sender,
        ModerationDecision decision)
    {
        if (_incomingEventCts.IsCancellationRequested) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!_incomingEventCts.IsCancellationRequested) IsSpeaking = false;
        });
    }

    [RelayCommand]
    public void ToggleArm()
    {
        if (_ttsQueue.IsArmed)
        {
            DisarmSafeSpeak();
            return;
        }

        ArmSafeSpeak();
    }

    [RelayCommand]
    public void UseAutomaticPlayback()
    {
        if (!_ttsQueue.IsArmed)
        {
            AnnounceState(
                "SafeSpeak is disarmed. Arm SafeSpeak before choosing automatic playback.");
            return;
        }

        _ttsQueue.ResumeAutomatic();
        AnnounceState("Automatic playback enabled.");
    }

    [RelayCommand]
    public void UseManualPlayback()
    {
        if (!_ttsQueue.IsArmed)
        {
            AnnounceState(
                "SafeSpeak is disarmed. Arm SafeSpeak before choosing manual playback.");
            return;
        }

        _ttsQueue.UseManualAdvance();
        AnnounceState(
            "Manual playback enabled. Use Speak next approved message to advance one item at a time.");
    }

    [RelayCommand]
    public void PauseOrResumeTts()
    {
        if (!_ttsQueue.IsArmed)
        {
            AnnounceState(
                "SafeSpeak is disarmed. There is no text to speech playback to pause.");
            return;
        }

        if (_ttsQueue.Mode == TtsPlaybackMode.Paused)
        {
            _ttsQueue.ResumeAutomatic();
            AnnounceState("Text to speech resumed in automatic mode.");
            return;
        }

        _ttsQueue.SetPaused(true);
        AnnounceState(
            $"Text to speech paused. The current message may finish. {PauseRoutingSummary}");
    }

    [RelayCommand]
    public void StopCurrentSpeech()
    {
        _ttsQueue.StopCurrentSpeech();
        AnnounceState("Current speech stopped.");
    }

    [RelayCommand]
    public void EmergencyStop()
    {
        Interlocked.Increment(ref _monitoringGeneration);
        _ttsQueue.EmergencyStop();
        _announcer.PlayCue(SoundCueType.EmergencyStop);
        AnnounceState(
            "Emergency stop activated. Current speech stopped, the queue was cleared, and SafeSpeak is disarmed. Re-arm SafeSpeak to resume monitoring.",
            interrupt: true);
    }

    [RelayCommand]
    public void ClearQueue()
    {
        _ttsQueue.ClearQueue();
        _announcer.PlayCue(SoundCueType.QueueEmpty);
        AnnounceState("Text to speech queue cleared.");
    }

    [RelayCommand]
    public async Task SpeakNextApprovedMessage()
    {
        if (!_ttsQueue.IsArmed)
        {
            AnnounceState(
                "SafeSpeak is disarmed. Arm SafeSpeak before speaking a queued message.");
            return;
        }

        if (_ttsQueue.Mode != TtsPlaybackMode.Manual)
        {
            AnnounceState(
                "Choose manual playback before using Speak next approved message.");
            return;
        }

        if (!await _ttsQueue.PlayNextManualAsync())
        {
            AnnounceState("There are no approved messages waiting.");
        }
    }

    private void ArmSafeSpeak()
    {
        Interlocked.Increment(ref _monitoringGeneration);
        _sessionDonors.Clear();
        _ttsQueue.ArmAutomatic();
        _announcer.PlayCue(SoundCueType.Armed);
        AnnounceState(
            "SafeSpeak armed. Monitoring and automatic moderated text to speech are active.");
    }

    private void DisarmSafeSpeak()
    {
        Interlocked.Increment(ref _monitoringGeneration);
        _ttsQueue.Disarm();
        _announcer.PlayCue(SoundCueType.Disarmed);
        AnnounceState(
            "SafeSpeak disarmed. Incoming events are discarded until SafeSpeak is armed again.");
    }
}
