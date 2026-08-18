using SafeSpeak.Core.Chat;

namespace SafeSpeak.Core.Moderation;

public sealed class ModerationPipeline(
    Blocklist blocklist,
    IToxicityClassifier toxicityClassifier,
    ModerationOptions? options = null)
{
    private readonly ModerationOptions _options = options ?? new();

    public async ValueTask<ModerationDecision> EvaluateAsync(
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
        {
            return ModerationDecision.Reject(ModerationReason.Empty);
        }

        if (message.Text.Length > _options.MaximumMessageLength)
        {
            return ModerationDecision.Reject(ModerationReason.TooLong);
        }

        if ((_options.AllowedAudience & message.AudienceRole) == 0)
        {
            return ModerationDecision.Reject(ModerationReason.DisallowedAudience);
        }

        TextNormalizationResult normalized = TextNormalizer.Normalize(
            message.Text,
            _options.MaximumRepeatedCharacters);

        if (string.IsNullOrWhiteSpace(normalized.CleanText))
        {
            return ModerationDecision.Reject(ModerationReason.Empty);
        }

        if (!normalized.CleanText.Any(char.IsLetterOrDigit))
        {
            return ModerationDecision.Reject(ModerationReason.NoSpeakableText);
        }

        if (_options.RejectUrls && ContainsUrl(normalized.ComparisonText))
        {
            return ModerationDecision.Reject(ModerationReason.Url);
        }

        if (_options.EnglishOnly && normalized.Scripts.Any(script => script is not UnicodeScript.Latin))
        {
            return ModerationDecision.Reject(ModerationReason.DisallowedScript);
        }

        if (_options.RejectMixedScripts && normalized.HasMixedScripts)
        {
            return ModerationDecision.Reject(ModerationReason.MixedScripts);
        }

        if (blocklist.Contains(normalized))
        {
            return ModerationDecision.Reject(ModerationReason.BlockedTerm);
        }

        if (!_options.EnableClassifier)
        {
            return ModerationDecision.Approve(normalized.CleanText);
        }

        if (!toxicityClassifier.IsAvailable)
        {
            return _options.HoldWhenClassifierUnavailable
                ? ModerationDecision.Hold(ModerationReason.ClassifierUnavailable)
                : ModerationDecision.Reject(ModerationReason.ClassifierUnavailable);
        }

        double? score = await toxicityClassifier.ScoreAsync(normalized.CleanText, cancellationToken);
        if (score is null)
        {
            return _options.HoldWhenClassifierUnavailable
                ? ModerationDecision.Hold(ModerationReason.ClassifierUnavailable)
                : ModerationDecision.Reject(ModerationReason.ClassifierUnavailable);
        }

        return score >= _options.ToxicityThreshold
            ? new(ModerationDisposition.Rejected, ModerationReason.Toxicity, string.Empty, score)
            : ModerationDecision.Approve(normalized.CleanText, score);
    }

    private static bool ContainsUrl(string text) =>
        text.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("www.", StringComparison.OrdinalIgnoreCase);
}
