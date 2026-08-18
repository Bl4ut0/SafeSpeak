namespace SafeSpeak.Core.Moderation;

public sealed record TextNormalizationResult(
    string CleanText,
    string ComparisonText,
    string CompactComparisonText,
    IReadOnlySet<UnicodeScript> Scripts,
    bool HadInvisibleCharacters)
{
    public bool HasMixedScripts => Scripts.Count > 1;
}
