using SafeSpeak.Core.Chat;

namespace SafeSpeak.Core.Moderation;

public sealed record ModerationOptions
{
    public int MaximumMessageLength { get; init; } = 200;

    public int MaximumRepeatedCharacters { get; init; } = 2;

    public bool EnglishOnly { get; init; } = true;

    public bool RejectMixedScripts { get; init; } = true;

    public bool RejectUrls { get; init; } = true;

    public bool EnableClassifier { get; init; }

    public bool HoldWhenClassifierUnavailable { get; init; } = true;

    public double ToxicityThreshold { get; init; } = 0.85;

    public AudienceRole AllowedAudience { get; init; } = AudienceRole.Guest |
                                                          AudienceRole.Follower |
                                                          AudienceRole.Subscriber |
                                                          AudienceRole.Moderator |
                                                          AudienceRole.Trusted;
}
