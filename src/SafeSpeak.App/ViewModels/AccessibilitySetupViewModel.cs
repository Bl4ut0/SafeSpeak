using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Models;

namespace SafeSpeak.App.ViewModels;

public sealed partial class AccessibilitySetupViewModel : ObservableObject, IDisposable
{
    private const string InitialQuestion =
        "Would you like SafeSpeak's integrated reader and spoken guidance enabled? " +
        "Choose Yes to hear private application guidance, or No to use your Windows screen reader without extra SafeSpeak speech.";

    private readonly AppSettings _settings;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Action _onCompleted;
    private readonly ReaderSetupConfirmation _confirmation = new();
    private readonly bool _previousAnnouncerState;

    public ScreenReaderAnnouncer Announcer => _announcer;
    private bool _completed;

    [ObservableProperty]
    private string _promptText = InitialQuestion;

    [ObservableProperty]
    private string _answerProgress = "Answer 1 of 2";

    [ObservableProperty]
    private string _statusText =
        "Your preference is saved only after two matching answers. If the answers differ, confirmation starts again.";

    public event EventHandler? FocusRequested;

    public AccessibilitySetupViewModel(
        AppSettings settings,
        ScreenReaderAnnouncer announcer,
        Action onCompleted)
    {
        _settings = settings;
        _announcer = announcer;
        _onCompleted = onCompleted;
        _previousAnnouncerState = announcer.IsEnhancedAccessibilityEnabled;

        // Setup must be audible even when the previously saved choice was off.
        // Only fixed application instructions are spoken here—never chat content.
        _announcer.IsEnhancedAccessibilityEnabled = true;
        SpeakCurrentPrompt();
    }

    [RelayCommand]
    private void AnswerYes() => SubmitAnswer(enableReader: true);

    [RelayCommand]
    private void AnswerNo() => SubmitAnswer(enableReader: false);

    private void SubmitAnswer(bool enableReader)
    {
        ReaderSetupResult result = _confirmation.Submit(enableReader);

        switch (result)
        {
            case ReaderSetupResult.AwaitingMatchingAnswer:
                AnswerProgress = "Answer 2 of 2";
                StatusText = $"First answer: {(enableReader ? "Yes" : "No")}. Answer the same question once more to save it.";
                PromptText = $"Please confirm. {InitialQuestion}";
                SpeakCurrentPrompt();
                break;

            case ReaderSetupResult.MismatchRestarted:
                AnswerProgress = "Answer 1 of 2";
                StatusText = "The two answers did not match, so nothing was saved. Starting again.";
                PromptText = InitialQuestion;
                _announcer.Announce(
                    $"The answers did not match. Nothing was saved. Starting again. {InitialQuestion} " +
                    "Press Y for Yes or N for No.",
                    interrupt: true);
                break;

            case ReaderSetupResult.ConfirmedEnabled:
                Complete(enableReader: true);
                return;

            case ReaderSetupResult.ConfirmedDisabled:
                Complete(enableReader: false);
                return;
        }

        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Complete(bool enableReader)
    {
        _settings.AccessibilityProfile = enableReader
            ? AccessibilityProfile.FullVoiceGuided
            : AccessibilityProfile.StandardVisual;
        _settings.HasCompletedAccessibilitySetup = true;

        if (!_settings.TrySave(out string? error))
        {
            _settings.HasCompletedAccessibilitySetup = false;
            AnswerProgress = "Preference not saved";
            StatusText = $"SafeSpeak could not save this setting. {error}";
            PromptText = InitialQuestion;
            _announcer.Announce(
                "SafeSpeak could not save the reader preference. Nothing was changed. Please try again.",
                interrupt: true);
            FocusRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _completed = true;
        string result = enableReader ? "enabled" : "disabled";
        _announcer.Announce(
            $"Two matching answers received. SafeSpeak integrated reader {result}. Preference saved.",
            interrupt: true);
        _announcer.IsEnhancedAccessibilityEnabled = enableReader;
        _onCompleted();
    }

    private void SpeakCurrentPrompt()
    {
        _announcer.Announce(
            $"{AnswerProgress}. {PromptText} Press Y for Yes or N for No. Press Tab to move between controls.",
            interrupt: true);
    }

    public void Dispose()
    {
        if (!_completed)
        {
            _announcer.IsEnhancedAccessibilityEnabled = _previousAnnouncerState;
        }
    }
}
