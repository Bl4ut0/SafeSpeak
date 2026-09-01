namespace SafeSpeak.Core.AI;

public sealed record IntentClassificationResult
{
    public bool IsToxic { get; init; }
    public double ToxicityScore { get; init; }
    public double SevereToxicityScore { get; init; }
    public double ObsceneScore { get; init; }
    public double ThreatScore { get; init; }
    public double HarassmentScore { get; init; }
    public double InsultScore { get; init; }
    public double IdentityHateScore { get; init; }
    public string FlaggedCategory { get; init; } = "None";
    public string ModelUsed { get; init; } = "None";
}

/// <summary>
/// Contract for evaluating subtle toxicity, insults, threats, and harassment.
/// </summary>
public interface IIntentClassifier : IDisposable
{
    string ModelName { get; }
    bool IsModelLoaded { get; }
    Task<IntentClassificationResult> ClassifyAsync(string text, CancellationToken cancellationToken = default);
}
