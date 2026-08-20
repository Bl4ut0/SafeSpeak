namespace SafeSpeak.Core.Models;

/// <summary>
/// Final action decided by the moderation pipeline.
/// </summary>
public enum ModerationDisposition
{
    Approved = 0,
    HeldForManualReview = 1,
    Rejected = 2
}

/// <summary>
/// Categorized reason code for rejection or flagging.
/// </summary>
public enum ModerationReasonCode
{
    None = 0,
    AudienceRestricted = 1,
    UserCooldown = 2,
    MessageTooLong = 3,
    DisallowedScript = 4,
    BlockedTerm = 5,
    HomoglyphEvasion = 6,
    SevereToxicity = 7,
    ThreatOrHarassment = 8,
    SpamPattern = 9,
    UnsafeUrl = 10,
    SystemDisarmed = 11
}

/// <summary>
/// Complete decision payload returned by the moderation pipeline.
/// </summary>
public sealed record ModerationDecision
{
    public required ChatMessage Message { get; init; }
    public ModerationDisposition Disposition { get; init; }
    public ModerationReasonCode ReasonCode { get; init; }
    public string ReasonDescription { get; init; } = string.Empty;
    public string SpokenText { get; init; } = string.Empty;
    public string NormalizedText { get; init; } = string.Empty;
    public double ToxicityScore { get; init; }
    public IReadOnlyList<string> TriggeredRules { get; init; } = Array.Empty<string>();
    public bool Passed => Disposition == ModerationDisposition.Approved;
    public string SafeAuthorDisplayName { get; init; } = "A viewer";
    public string SafeDisplayText { get; init; } = "Content hidden for safety.";
    public string SafeReasonDescription => ReasonCode switch
    {
        ModerationReasonCode.None => Passed ? "Approved" : "Not approved",
        ModerationReasonCode.AudienceRestricted => "Sender is not in the allowed audience.",
        ModerationReasonCode.UserCooldown => "Sender is in the message cooldown period.",
        ModerationReasonCode.MessageTooLong => "Message is longer than the configured limit.",
        ModerationReasonCode.DisallowedScript => "Message uses a blocked or mixed writing system.",
        ModerationReasonCode.BlockedTerm => "Message matched a blocked safety rule.",
        ModerationReasonCode.HomoglyphEvasion => "Message contains suspected character substitution.",
        ModerationReasonCode.SevereToxicity => "Message was rejected by the contextual safety layer.",
        ModerationReasonCode.ThreatOrHarassment => "Message contains suspected threat or harassment content.",
        ModerationReasonCode.SpamPattern => "Message matched a spam pattern.",
        ModerationReasonCode.UnsafeUrl => "Message contains an unsafe link.",
        ModerationReasonCode.SystemDisarmed => "SafeSpeak is disarmed.",
        _ => "Message was not approved."
    };
    public string AccessibleSummary => Passed
        ? $"Approved message from {SafeAuthorDisplayName}. {SafeDisplayText}"
        : $"{Disposition}. Content hidden for safety. Reason: {SafeReasonDescription}";
}
