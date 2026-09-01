using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Moderation;

/// <summary>
/// Multi-tiered moderation pipeline orchestrating deterministic anti-evasion rules and AI intent classification.
/// </summary>
public sealed class ModerationPipeline : IDisposable
{
    private readonly RuleEngine _ruleEngine;
    private IIntentClassifier _intentClassifier;
    private readonly object _classifierLifetimeLock = new();
    private readonly List<IIntentClassifier> _retiredClassifiers = new();
    private int _disposed;

    public ModerationConfig Config { get; }
    public RuleEngine Rules => _ruleEngine;
    public IIntentClassifier Classifier => Volatile.Read(ref _intentClassifier);

    public ModerationPipeline(
        ModerationConfig? config = null,
        RuleEngine? ruleEngine = null,
        IIntentClassifier? intentClassifier = null)
    {
        Config = config ?? new ModerationConfig();
        _ruleEngine = ruleEngine ?? new RuleEngine();
        _intentClassifier = intentClassifier ?? new LocalOnnxIntentClassifier();
    }

    /// <summary>
    /// Swaps the active intent classification engine at runtime without
    /// disposing an engine that an in-flight message may still be using.
    /// </summary>
    public void SetIntentClassifier(IIntentClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        lock (_classifierLifetimeLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var old = Interlocked.Exchange(ref _intentClassifier, classifier);
            if (!ReferenceEquals(old, classifier))
            {
                _retiredClassifiers.Add(old);
            }
        }
    }

    /// <summary>
    /// Processes an incoming chat message and produces a final moderation disposition and cleaned spoken text.
    /// </summary>
    public async Task<ModerationDecision> ProcessMessageAsync(
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (message == null || string.IsNullOrWhiteSpace(message.RawText))
        {
            return new ModerationDecision
            {
                Message = message ?? new ChatMessage(),
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.None,
                ReasonDescription = "Empty message payload",
                SpokenText = string.Empty,
                NormalizedText = string.Empty
            };
        }

        // 1. Length Validation
        if (message.RawText.Length > Config.MaxMessageLength)
        {
            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.MessageTooLong,
                ReasonDescription = $"Message exceeds maximum allowed length of {Config.MaxMessageLength} characters",
                SpokenText = string.Empty,
                NormalizedText = message.RawText
            };
        }

        // 2. Audience Eligibility Rule
        if (!_ruleEngine.IsAudienceEligible(
                message,
                Config.AudienceMode,
                Config.AllowDonorsToSpeak))
        {
            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.AudienceRestricted,
                ReasonDescription = $"Sender tier ({message.AuthorTier}) does not meet the audience requirement ({Config.AudienceMode})",
                SpokenText = string.Empty,
                NormalizedText = message.RawText
            };
        }

        // 3. User Cooldown Rule
        if (_ruleEngine.IsUserInCooldown(message.Author, Config.UserCooldownSeconds, DateTimeOffset.UtcNow))
        {
            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.UserCooldown,
                ReasonDescription = $"Sender is in active cooldown ({Config.UserCooldownSeconds}s)",
                SpokenText = string.Empty,
                NormalizedText = message.RawText
            };
        }

        // 4. Script & Language Validation
        if (Config.RejectMixedScripts && ScriptValidator.ContainsMixedScriptWords(message.RawText))
        {
            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.DisallowedScript,
                ReasonDescription = "Mixed writing systems detected in single word (homoglyph spoofing attempt)",
                SpokenText = string.Empty,
                NormalizedText = message.RawText
            };
        }

        if (Config.EnglishOnly && !ScriptValidator.IsLatinOrEmojiOnly(message.RawText))
        {
            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.DisallowedScript,
                ReasonDescription = "Non-Latin script detected while English-only mode is active",
                SpokenText = string.Empty,
                NormalizedText = message.RawText
            };
        }

        // 5. Multi-layer Deobfuscation for Security Inspection
        string normalizedForInspection = UnicodeNormalizer.NormalizeForInspection(message.RawText);

        // 6. Blocklist / Prohibited Rule Matching
        if (_ruleEngine.MatchesBlockedTerms(
            normalizedForInspection,
            Config.CustomBlockedTerms,
            Config.CustomAllowedTerms,
            out string matchedTerm))
        {
            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.BlockedTerm,
                ReasonDescription = $"Matches prohibited term or pattern: '{matchedTerm}'",
                SpokenText = string.Empty,
                NormalizedText = normalizedForInspection,
                TriggeredRules = new[] { matchedTerm }
            };
        }

        // 7. Intent classification. The deterministic blocklist above is always
        // enforced; the user-facing moderation level controls only uncertain
        // contextual hostility.
        IntentClassificationResult intentResult;
        try
        {
            intentResult = await _intentClassifier.ClassifyAsync(
                normalizedForInspection,
                cancellationToken);
            if (!HasValidIntentScores(intentResult))
            {
                throw new InvalidDataException("The contextual safety layer returned invalid scores.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.SevereToxicity,
                ReasonDescription = "The contextual safety layer was unavailable",
                SpokenText = string.Empty,
                NormalizedText = normalizedForInspection
            };
        }
        double toxicityScore = intentResult.ToxicityScore;

        if (toxicityScore >= Config.IntentToxicityThreshold)
        {
            var reasonCode = intentResult.ThreatScore > 0.7 ||
                             intentResult.HarassmentScore > 0.7
                ? ModerationReasonCode.ThreatOrHarassment
                : ModerationReasonCode.SevereToxicity;

            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = reasonCode,
                ReasonDescription =
                    $"Intent filter flagged {intentResult.FlaggedCategory} at moderation level {Math.Clamp(Config.IntentModerationLevel, 1, 4)} (score {toxicityScore:0.00} >= threshold {Config.IntentToxicityThreshold:0.00})",
                SpokenText = string.Empty,
                NormalizedText = normalizedForInspection,
                ToxicityScore = toxicityScore,
                TriggeredRules = new[] { intentResult.FlaggedCategory }
            };
        }

        // 8. Prepare Cleaned Spoken Output
        string speechCleaned = UnicodeNormalizer.CleanForSpeech(message.RawText, Config.StripUrls);

        string safeDisplayName = await GetSafeDisplayNameAsync(
            message.AuthorDisplayName,
            cancellationToken);
        string finalSpokenText = message.AttributionStyle == SpokenAttributionStyle.LeadingName
            ? $"{safeDisplayName} {speechCleaned}"
            : $"{safeDisplayName} says: {speechCleaned}";

        return new ModerationDecision
        {
            Message = message,
            Disposition = ModerationDisposition.Approved,
            ReasonCode = ModerationReasonCode.None,
            ReasonDescription = "Approved",
            SpokenText = finalSpokenText,
            SafeAuthorDisplayName = safeDisplayName,
            SafeDisplayText = speechCleaned,
            NormalizedText = normalizedForInspection,
            ToxicityScore = toxicityScore
        };
    }

    private async Task<string> GetSafeDisplayNameAsync(
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 50)
        {
            return "A viewer";
        }

        if (ScriptValidator.ContainsMixedScriptWords(displayName) ||
            (Config.EnglishOnly && !ScriptValidator.IsLatinOrEmojiOnly(displayName)))
        {
            return "A viewer";
        }

        string normalized = UnicodeNormalizer.NormalizeForInspection(displayName);
        if (_ruleEngine.MatchesBlockedTerms(
                normalized,
                Config.CustomBlockedTerms,
                Config.CustomAllowedTerms,
                out _))
        {
            return "A viewer";
        }

        try
        {
            IntentClassificationResult nameIntent = await _intentClassifier.ClassifyAsync(
                normalized,
                cancellationToken);
            if (!HasValidIntentScores(nameIntent) ||
                nameIntent.ToxicityScore >= Config.IntentToxicityThreshold)
            {
                return "A viewer";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A display name is optional attribution. Fail closed to a neutral label.
            return "A viewer";
        }

        string cleaned = UnicodeNormalizer.CleanForSpeech(displayName, stripUrls: true);
        return string.IsNullOrWhiteSpace(cleaned) ? "A viewer" : cleaned;
    }

    private static bool HasValidIntentScores(IntentClassificationResult? result)
    {
        if (result is null) return false;

        return IsProbability(result.ToxicityScore) &&
               IsProbability(result.SevereToxicityScore) &&
               IsProbability(result.ObsceneScore) &&
               IsProbability(result.ThreatScore) &&
               IsProbability(result.HarassmentScore) &&
               IsProbability(result.InsultScore) &&
               IsProbability(result.IdentityHateScore);
    }

    private static bool IsProbability(double value) =>
        double.IsFinite(value) && value is >= 0 and <= 1;

    public void Dispose()
    {
        List<IIntentClassifier> classifiers;
        lock (_classifierLifetimeLock)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            classifiers = new List<IIntentClassifier>(_retiredClassifiers.Count + 1)
            {
                _intentClassifier
            };
            classifiers.AddRange(_retiredClassifiers);
            _retiredClassifiers.Clear();
        }

        var disposedClassifiers = new HashSet<IIntentClassifier>(ReferenceEqualityComparer.Instance);
        foreach (IIntentClassifier classifier in classifiers)
        {
            if (disposedClassifiers.Add(classifier))
            {
                classifier.Dispose();
            }
        }
    }
}
