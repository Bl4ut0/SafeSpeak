namespace SafeSpeak.Core.Moderation;

public enum ModerationDisposition
{
    Approved,
    Held,
    Rejected,
}

public enum ModerationReason
{
    None,
    Empty,
    TooLong,
    NoSpeakableText,
    Url,
    DisallowedAudience,
    RateLimited,
    Duplicate,
    DisallowedScript,
    MixedScripts,
    BlockedTerm,
    Toxicity,
    ClassifierUnavailable,
}

public sealed record ModerationDecision(
    ModerationDisposition Disposition,
    ModerationReason Reason,
    string SpeakableText,
    double? ToxicityScore = null)
{
    public static ModerationDecision Approve(string text, double? score = null) =>
        new(ModerationDisposition.Approved, ModerationReason.None, text, score);

    public static ModerationDecision Hold(ModerationReason reason, string text = "") =>
        new(ModerationDisposition.Held, reason, text);

    public static ModerationDecision Reject(ModerationReason reason) =>
        new(ModerationDisposition.Rejected, reason, string.Empty);
}
