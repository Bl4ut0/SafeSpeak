using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Moderation;

/// <summary>
/// Multi-tiered moderation pipeline orchestrating deterministic anti-evasion rules and AI intent classification.
/// </summary>
public sealed class ModerationPipeline
{
    private readonly RuleEngine _ruleEngine;
    private readonly IIntentClassifier _intentClassifier;

    public ModerationConfig Config { get; }
    public RuleEngine Rules => _ruleEngine;
    public IIntentClassifier Classifier => _intentClassifier;

    public ModerationPipeline(
        ModerationConfig? config = null,
        RuleEngine? ruleEngine = null,
        IIntentClassifier? intentClassifier = null)
    {
        Config = config ?? new ModerationConfig();
        _ruleEngine = ruleEngine ?? new RuleEngine();
        _intentClassifier = intentClassifier ?? new HeuristicIntentClassifier();
    }

    /// <summary>
    /// Processes an incoming chat message and produces a final moderation disposition and cleaned spoken text.
    /// </summary>
    public async Task<ModerationDecision> ProcessMessageAsync(
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
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
        if (!_ruleEngine.IsAudienceEligible(message, Config.AudienceMode))
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

        // 7. Tier 2: AI / SLM Intent Classification
        double toxicityScore = 0.0;
        if (Config.AiClassificationEnabled)
        {
            var aiResult = await _intentClassifier.ClassifyAsync(normalizedForInspection, cancellationToken);
            toxicityScore = aiResult.ToxicityScore;

            if (aiResult.IsToxic || toxicityScore >= Config.AiToxicityThreshold)
            {
                var reasonCode = aiResult.ThreatScore > 0.7
                    ? ModerationReasonCode.ThreatOrHarassment
                    : ModerationReasonCode.SevereToxicity;

                return new ModerationDecision
                {
                    Message = message,
                    Disposition = ModerationDisposition.Rejected,
                    ReasonCode = reasonCode,
                    ReasonDescription = $"AI Intent Classifier flagged content as {aiResult.FlaggedCategory} (Score: {toxicityScore:P0})",
                    SpokenText = string.Empty,
                    NormalizedText = normalizedForInspection,
                    ToxicityScore = toxicityScore,
                    TriggeredRules = new[] { aiResult.FlaggedCategory }
                };
            }
        }

        // 8. Prepare Cleaned Spoken Output
        string speechCleaned = UnicodeNormalizer.CleanForSpeech(message.RawText, Config.StripUrls);

        string safeDisplayName = GetSafeDisplayName(message.AuthorDisplayName);
        string finalSpokenText = Config.SpeakUsernames
            ? $"{safeDisplayName} says: {speechCleaned}"
            : speechCleaned;

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

    private string GetSafeDisplayName(string? displayName)
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

        string cleaned = UnicodeNormalizer.CleanForSpeech(displayName, stripUrls: true);
        return string.IsNullOrWhiteSpace(cleaned) ? "A viewer" : cleaned;
    }
}
