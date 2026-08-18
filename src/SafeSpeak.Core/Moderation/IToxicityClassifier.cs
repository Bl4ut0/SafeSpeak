namespace SafeSpeak.Core.Moderation;

public interface IToxicityClassifier
{
    bool IsAvailable { get; }

    ValueTask<double?> ScoreAsync(string text, CancellationToken cancellationToken = default);
}

public sealed class UnavailableToxicityClassifier : IToxicityClassifier
{
    public bool IsAvailable => false;

    public ValueTask<double?> ScoreAsync(string text, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<double?>(null);
}
