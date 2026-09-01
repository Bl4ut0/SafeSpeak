using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public sealed class IntentScoreValidationTests
{
    public static IEnumerable<object[]> InvalidScores()
    {
        double[] invalidValues =
            [double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.01, 1.01];

        foreach (ScoreField field in Enum.GetValues<ScoreField>())
        {
            foreach (double value in invalidValues)
            {
                yield return [field, value];
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidScores))]
    public async Task InvalidClassifierScoreRejectsMessageAndNeutralizesName(
        ScoreField field,
        double invalidValue)
    {
        IntentClassificationResult invalid = WithScore(field, invalidValue);

        using (var classifier = new SequenceClassifier(invalid))
        {
            var pipeline = CreatePipeline(classifier);
            ModerationDecision decision = await pipeline.ProcessMessageAsync(Message());

            Assert.False(decision.Passed);
            Assert.Empty(decision.SpokenText);
            Assert.Equal(ModerationReasonCode.SevereToxicity, decision.ReasonCode);
        }

        using (var classifier = new SequenceClassifier(ValidResult(), invalid))
        {
            var pipeline = CreatePipeline(classifier);
            ModerationDecision decision = await pipeline.ProcessMessageAsync(Message());

            Assert.True(decision.Passed);
            Assert.Equal("A viewer", decision.SafeAuthorDisplayName);
            Assert.Equal("A viewer says: Hello everyone", decision.SpokenText);
        }
    }

    private static ModerationPipeline CreatePipeline(IIntentClassifier classifier) =>
        new(
            new ModerationConfig
            {
                IntentModerationLevel = 3,
                UserCooldownSeconds = 0
            },
            intentClassifier: classifier);

    private static ChatMessage Message() => new()
    {
        Author = "viewer-id",
        AuthorDisplayName = "Friendly Viewer",
        RawText = "Hello everyone"
    };

    private static IntentClassificationResult ValidResult() => new()
    {
        FlaggedCategory = "None",
        ModelUsed = "Test classifier"
    };

    private static IntentClassificationResult WithScore(ScoreField field, double value)
    {
        IntentClassificationResult result = ValidResult();
        return field switch
        {
            ScoreField.Toxicity => result with { ToxicityScore = value },
            ScoreField.SevereToxicity => result with { SevereToxicityScore = value },
            ScoreField.Obscene => result with { ObsceneScore = value },
            ScoreField.Threat => result with { ThreatScore = value },
            ScoreField.Harassment => result with { HarassmentScore = value },
            ScoreField.Insult => result with { InsultScore = value },
            ScoreField.IdentityHate => result with { IdentityHateScore = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    public enum ScoreField
    {
        Toxicity,
        SevereToxicity,
        Obscene,
        Threat,
        Harassment,
        Insult,
        IdentityHate
    }

    private sealed class SequenceClassifier(params IntentClassificationResult[] results)
        : IIntentClassifier
    {
        private int _index;

        public string ModelName => "Sequence test classifier";
        public bool IsModelLoaded => true;

        public Task<IntentClassificationResult> ClassifyAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Math.Min(Interlocked.Increment(ref _index) - 1, results.Length - 1);
            return Task.FromResult(results[index]);
        }

        public void Dispose() { }
    }
}
