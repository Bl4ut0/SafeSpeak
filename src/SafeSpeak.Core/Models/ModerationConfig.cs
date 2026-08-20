namespace SafeSpeak.Core.Models;

/// <summary>
/// Audience tier requirements for chat-to-speech.
/// </summary>
public enum AudienceMode
{
    All = 0,
    FollowersOnly = 1,
    SubscribersOnly = 2,
    ModeratorsOnly = 3
}

/// <summary>
/// Moderation sensitivity preset.
/// </summary>
public enum ModerationStrictness
{
    Standard = 0,
    High = 1,
    Maximum = 2
}

/// <summary>
/// Configuration for the SafeSpeak moderation pipeline.
/// </summary>
public sealed class ModerationConfig
{
    public AudienceMode AudienceMode { get; set; } = AudienceMode.All;
    public ModerationStrictness Strictness { get; set; } = ModerationStrictness.High;
    public int MaxMessageLength { get; set; } = 200;
    public int UserCooldownSeconds { get; set; } = 5;
    public bool EnglishOnly { get; set; } = true;
    public bool RejectMixedScripts { get; set; } = true;
    public bool StripUrls { get; set; } = true;
    public bool SpeakUsernames { get; set; } = false;
    public bool AiClassificationEnabled { get; set; } = false;
    public double AiToxicityThreshold { get; set; } = 0.65;
    public List<string> CustomBlockedTerms { get; set; } = new();
    public List<string> CustomAllowedTerms { get; set; } = new();
}
