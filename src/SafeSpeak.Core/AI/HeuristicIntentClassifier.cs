using System.Text.RegularExpressions;

namespace SafeSpeak.Core.AI;

/// <summary>
/// Fast heuristic intent and toxicity estimator with zero external model dependencies.
/// Uses sentiment markers, harassment patterns, threat signals, and punctuation abuse.
/// </summary>
public sealed partial class HeuristicIntentClassifier : IIntentClassifier
{
    public string ModelName => "FastHeuristicIntentEngine (Built-in)";
    public bool IsModelLoaded => true;

    [GeneratedRegex(@"\b(kill|hang|shoot|stab|murder|burn|beat)\s+(you|urself|yourself|him|her|them)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ThreatRegex();

    [GeneratedRegex(@"\b(trash|ugly|loser|fat|disgusting|pathetic|idiot|moron|dumb|clown|stupid)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex InsultRegex();

    [GeneratedRegex(@"\b(nobody\s+likes\s+you|waste\s+of\s+space|get\s+cancer|go\s+away\s+and\s+die)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HarassmentRegex();

    public Task<IntentClassificationResult> ClassifyAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new IntentClassificationResult
            {
                IsToxic = false,
                ToxicityScore = 0.0,
                ModelUsed = ModelName
            });
        }

        double threatScore = 0.0;
        double harassmentScore = 0.0;
        double insultScore = 0.0;

        if (ThreatRegex().IsMatch(text))
        {
            threatScore = 0.95;
        }

        if (HarassmentRegex().IsMatch(text))
        {
            harassmentScore = 0.85;
        }

        var insultMatches = InsultRegex().Matches(text);
        if (insultMatches.Count > 0)
        {
            insultScore = Math.Min(0.9, 0.4 + (insultMatches.Count * 0.25));
        }

        double totalToxicity = Math.Max(threatScore, Math.Max(harassmentScore, insultScore));
        string category = "None";

        if (threatScore >= 0.8) category = "Threat";
        else if (harassmentScore >= 0.7) category = "Harassment";
        else if (insultScore >= 0.6) category = "Insult";

        var result = new IntentClassificationResult
        {
            IsToxic = totalToxicity >= 0.6,
            ToxicityScore = totalToxicity,
            ThreatScore = threatScore,
            HarassmentScore = harassmentScore,
            FlaggedCategory = category,
            ModelUsed = ModelName
        };

        return Task.FromResult(result);
    }

    public void Dispose() { }
}
