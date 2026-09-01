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
    public bool AllowDonorsToSpeak { get; set; } = true;
    /// <summary>
    /// Compatibility property. The main release always speaks a moderated
    /// author label so listeners can attribute chat safely.
    /// </summary>
    public bool SpeakUsernames { get; set; } = true;
    /// <summary>
    /// Legacy compatibility flag. Intent moderation is always active in the
    /// release experience; callers should use IntentModerationLevel.
    /// </summary>
    public bool AiClassificationEnabled { get; set; } = true;
    public double AiToxicityThreshold { get; set; } = 0.65;
    /// <summary>
    /// User-facing hostility filter strength: 1 Relaxed, 2 Balanced,
    /// 3 Strong, 4 Maximum.
    /// </summary>
    public int IntentModerationLevel { get; set; } = 3;
    public List<string> CustomBlockedTerms { get; set; } = new();
    public List<string> CustomAllowedTerms { get; set; } = new();

    public double IntentToxicityThreshold => GetIntentToxicityThreshold(IntentModerationLevel);

    public static double GetIntentToxicityThreshold(int level) => Math.Clamp(level, 1, 4) switch
    {
        1 => 0.90,
        2 => 0.75,
        3 => 0.60,
        4 => 0.45,
        _ => 0.60
    };
}
