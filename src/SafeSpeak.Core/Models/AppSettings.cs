using System.Text.Json;
using System.Text.Json.Serialization;

namespace SafeSpeak.Core.Models;

public enum AccessibilityProfile
{
    Unset = 0,
    FullVoiceGuided = 1,     // Blind Streamer: Full screen-reader announcements & audio earcons
    HighContrastVisual = 2,  // Low-Vision Streamer: Large high-contrast visuals, silent guidance
    StandardVisual = 3       // Sighted Streamer: Standard visual UI, no private voice chatter
}

public sealed class AppSettings
{
    public AccessibilityProfile AccessibilityProfile { get; set; } = AccessibilityProfile.Unset;
    public bool HasCompletedAccessibilitySetup { get; set; } = false;

    [JsonIgnore]
    public bool IsIntegratedReaderEnabled =>
        AccessibilityProfile == AccessibilityProfile.FullVoiceGuided;

    [JsonIgnore]
    public bool HasConfirmedReaderPreference =>
        HasCompletedAccessibilitySetup && AccessibilityProfile != AccessibilityProfile.Unset;

    public AudienceMode AudienceMode { get; set; } = AudienceMode.All;
    public ModerationStrictness Strictness { get; set; } = ModerationStrictness.High;
    public bool EnglishOnly { get; set; } = true;
    public bool RejectMixedScripts { get; set; } = true;
    public bool StripUrls { get; set; } = true;
    public bool SpeakUsernames { get; set; } = false;
    public bool AiClassificationEnabled { get; set; } = false;
    public double AiToxicityThreshold { get; set; } = 0.65;

    public bool AnnounceChatMessages { get; set; } = true;
    public bool AnnounceGifts { get; set; } = true;
    public bool AnnounceFollows { get; set; } = true;
    public bool AnnounceShares { get; set; } = true;
    public bool AnnounceSubscriptions { get; set; } = true;
    public bool AnnounceJoins { get; set; } = false;
    public bool AnnounceLikes { get; set; } = false;

    public bool BroadcastOutputEnabled { get; set; } = true;
    public bool PrivateMonitorEnabled { get; set; } = false;
    public bool MirrorApprovedMessagesToPrivateMonitor { get; set; } = true;
    public bool PrivateModerationNoticesEnabled { get; set; } = true;
    public bool UseHighContrastTheme { get; set; } = false;

    public string? SelectedAudioEndpointId { get; set; }
    public string? SelectedBroadcastEndpointId { get; set; }
    public string? SelectedPrivateEndpointId { get; set; }
    public string? SelectedVoiceName { get; set; }
    public int SpeechRate { get; set; } = 0;
    public int SpeechVolume { get; set; } = 100;

    public List<string> CustomBlockedTerms { get; set; } = new();

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SafeSpeak",
        "settings.json"
    );

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch { }

        return new AppSettings();
    }

    public ModerationConfig CreateModerationConfig() => new()
    {
        AudienceMode = AudienceMode,
        Strictness = Strictness,
        EnglishOnly = EnglishOnly,
        RejectMixedScripts = RejectMixedScripts,
        StripUrls = StripUrls,
        SpeakUsernames = SpeakUsernames,
        AiClassificationEnabled = AiClassificationEnabled,
        AiToxicityThreshold = Math.Clamp(AiToxicityThreshold, 0.3, 0.95),
        CustomBlockedTerms = new List<string>(CustomBlockedTerms)
    };

    public void CaptureModerationConfig(ModerationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        AudienceMode = config.AudienceMode;
        Strictness = config.Strictness;
        EnglishOnly = config.EnglishOnly;
        RejectMixedScripts = config.RejectMixedScripts;
        StripUrls = config.StripUrls;
        SpeakUsernames = config.SpeakUsernames;
        AiClassificationEnabled = config.AiClassificationEnabled;
        AiToxicityThreshold = Math.Clamp(config.AiToxicityThreshold, 0.3, 0.95);
        CustomBlockedTerms = new List<string>(config.CustomBlockedTerms);
    }

    public bool TrySave(out string? error)
    {
        try
        {
            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            string temporaryPath = SettingsFilePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsFilePath, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Save()
    {
        _ = TrySave(out _);
    }

    public void ResetAccessibilityProfile()
    {
        AccessibilityProfile = AccessibilityProfile.Unset;
        HasCompletedAccessibilitySetup = false;
        Save();
    }
}
