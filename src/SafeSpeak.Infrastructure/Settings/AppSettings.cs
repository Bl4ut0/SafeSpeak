namespace SafeSpeak.Infrastructure.Settings;

public enum AccessibilityMode
{
    FullyBlind,
    PartiallySighted,
    Standard,
}

public sealed record AppSettings
{
    public bool FirstRunComplete { get; init; }

    public AccessibilityMode AccessibilityMode { get; init; } = AccessibilityMode.FullyBlind;

    public bool EnglishOnly { get; init; } = true;

    public bool AutomaticPlayback { get; init; }
}
