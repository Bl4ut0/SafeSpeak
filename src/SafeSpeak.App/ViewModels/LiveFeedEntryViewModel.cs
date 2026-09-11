using CommunityToolkit.Mvvm.ComponentModel;
using SafeSpeak.Core.Models;

namespace SafeSpeak.App.ViewModels;

/// <summary>
/// Presents a moderation decision in the live activity list. Filtered source text
/// stays hidden until the user deliberately reveals that individual entry.
/// </summary>
public sealed partial class LiveFeedEntryViewModel : ObservableObject
{
    private readonly ModerationDecision _decision;

    [ObservableProperty]
    private bool _isFilteredContentRevealed;

    public LiveFeedEntryViewModel(ModerationDecision decision)
    {
        _decision = decision;
    }

    public ModerationDisposition Disposition => _decision.Disposition;
    public string SafeAuthorDisplayName => _decision.SafeAuthorDisplayName;
    public string ReviewAuthorDisplayName => IsFiltered && IsFilteredContentRevealed
        ? RawAuthorDisplayName
        : SafeAuthorDisplayName;
    public string PlatformName => _decision.Message.Platform;
    public string SafeDisplayText => _decision.SafeDisplayText;
    public string SafeReasonDescription => _decision.SafeReasonDescription;
    public bool IsFiltered => !_decision.Passed;

    public string ReviewDisplayText => IsFiltered && IsFilteredContentRevealed
        ? _decision.Message.RawText
        : SafeDisplayText;

    public string RevealInstruction => !IsFiltered
        ? string.Empty
        : IsFilteredContentRevealed
            ? "Filtered text is revealed. Double-click or press Enter to hide it."
            : "Filtered text is hidden. Double-click or press Enter to reveal it.";

    public string AccessibleSummary => !IsFiltered
        ? _decision.AccessibleSummary
        : IsFilteredContentRevealed
            ? $"{Disposition} message from {ReviewAuthorDisplayName} on {PlatformName}. Filtered content revealed: {_decision.Message.RawText}. Reason: {SafeReasonDescription}. Press Enter to hide it."
            : $"{_decision.AccessibleSummary} Press Enter to reveal the original filtered text.";

    public string RevealAnnouncement => IsFilteredContentRevealed
        ? $"Filtered content revealed. {ReviewAuthorDisplayName} on {PlatformName} said: {_decision.Message.RawText}. Reason: {SafeReasonDescription}."
        : "Filtered content hidden again.";

    public bool ToggleFilteredContent()
    {
        if (!IsFiltered)
        {
            return false;
        }

        IsFilteredContentRevealed = !IsFilteredContentRevealed;
        return true;
    }

    public void SetFilteredContentRevealed(bool revealed)
    {
        if (IsFiltered)
        {
            IsFilteredContentRevealed = revealed;
        }
    }

    partial void OnIsFilteredContentRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(ReviewDisplayText));
        OnPropertyChanged(nameof(ReviewAuthorDisplayName));
        OnPropertyChanged(nameof(RevealInstruction));
        OnPropertyChanged(nameof(AccessibleSummary));
        OnPropertyChanged(nameof(RevealAnnouncement));
    }


    private string RawAuthorDisplayName =>
        string.IsNullOrWhiteSpace(_decision.Message.AuthorDisplayName)
            ? string.IsNullOrWhiteSpace(_decision.Message.Author)
                ? "Unknown viewer"
                : _decision.Message.Author
            : _decision.Message.AuthorDisplayName;
}
