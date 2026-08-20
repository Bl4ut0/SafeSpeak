namespace SafeSpeak.Core.Accessibility;

/// <summary>
/// Collects a reader preference twice so an accidental key press cannot
/// permanently enable or disable spoken guidance.
/// </summary>
public sealed class ReaderSetupConfirmation
{
    private bool? _firstAnswer;

    public int AnswerNumber => _firstAnswer.HasValue ? 2 : 1;

    public ReaderSetupResult Submit(bool enableReader)
    {
        if (!_firstAnswer.HasValue)
        {
            _firstAnswer = enableReader;
            return ReaderSetupResult.AwaitingMatchingAnswer;
        }

        if (_firstAnswer.Value == enableReader)
        {
            bool confirmedChoice = _firstAnswer.Value;
            _firstAnswer = null;
            return confirmedChoice
                ? ReaderSetupResult.ConfirmedEnabled
                : ReaderSetupResult.ConfirmedDisabled;
        }

        _firstAnswer = null;
        return ReaderSetupResult.MismatchRestarted;
    }
}

public enum ReaderSetupResult
{
    AwaitingMatchingAnswer,
    MismatchRestarted,
    ConfirmedEnabled,
    ConfirmedDisabled
}
