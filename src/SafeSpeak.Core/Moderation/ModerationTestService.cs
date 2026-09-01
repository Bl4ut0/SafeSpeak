using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Moderation;

public enum ModerationTestOutcome
{
    Allowed = 0,
    Blocked = 1
}

/// <summary>
/// A privacy-safe projection of a production moderation decision. It never
/// includes the submitted, normalized, spoken, matched-rule, or model text.
/// </summary>
public sealed record ModerationTestResult(
    ModerationTestOutcome Outcome,
    string Category,
    string Reason)
{
    public bool IsAllowed => Outcome == ModerationTestOutcome.Allowed;

    public string AccessibleSummary => IsAllowed
        ? "Allowed — this message passes the current safety settings."
        : $"Blocked — {Category}. {Reason}";
}

/// <summary>
/// Evaluates an operator-supplied sample through the active production
/// moderation pipeline without sending it to any queue, feed, connector,
/// speech, or log. The injected pipeline remains owned by its caller.
/// </summary>
public sealed class ModerationTestService
{
    private readonly ModerationPipeline _pipeline;

    public ModerationTestService(ModerationPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public async Task<ModerationTestResult> EvaluateAsync(
        string? sample,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ModerationDecision decision = await _pipeline.ProcessMessageAsync(
            new ChatMessage
            {
                // Blank identity intentionally opts out of RuleEngine cooldown
                // state, while Host ensures the sample reaches safety rules
                // regardless of the configured audience tier.
                Author = string.Empty,
                AuthorDisplayName = string.Empty,
                AuthorTier = AuthorTier.Host,
                RawText = sample ?? string.Empty,
                Platform = "Local safety test"
            },
            cancellationToken);

        bool allowed = decision.Disposition == ModerationDisposition.Approved;
        return new ModerationTestResult(
            allowed
                ? ModerationTestOutcome.Allowed
                : ModerationTestOutcome.Blocked,
            GetSafeCategory(decision.ReasonCode, allowed),
            decision.SafeReasonDescription);
    }

    private static string GetSafeCategory(
        ModerationReasonCode reasonCode,
        bool allowed) => reasonCode switch
    {
        ModerationReasonCode.None => allowed ? "Allowed" : "Safety check",
        ModerationReasonCode.AudienceRestricted => "Audience eligibility",
        ModerationReasonCode.UserCooldown => "Message frequency",
        ModerationReasonCode.MessageTooLong => "Message length",
        ModerationReasonCode.DisallowedScript => "Writing system",
        ModerationReasonCode.BlockedTerm => "Blocked safety rule",
        ModerationReasonCode.HomoglyphEvasion => "Character substitution",
        ModerationReasonCode.SevereToxicity => "Contextual safety",
        ModerationReasonCode.ThreatOrHarassment => "Threat or harassment",
        ModerationReasonCode.SpamPattern => "Spam pattern",
        ModerationReasonCode.UnsafeUrl => "Link safety",
        ModerationReasonCode.SystemDisarmed => "System safety",
        _ => "Safety check"
    };
}
