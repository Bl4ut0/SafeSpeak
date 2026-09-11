using System.Text;
using System.Text.RegularExpressions;
using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Moderation;

/// <summary>
/// Multi-tiered moderation pipeline orchestrating deterministic anti-evasion rules and AI intent classification.
/// </summary>
public sealed partial class ModerationPipeline : IDisposable
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
        _intentClassifier = intentClassifier ?? IntentClassifierDefaults.CreateLocal();
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
        CancellationToken cancellationToken = default,
        bool isSystemEvent = false)
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

        // 3. Adjustable per-viewer and whole-stream sliding rate limits.
        // Positive community contributions (donations and follows) bypass rate limiting.
        bool exemptFromRateLimit = isSystemEvent || message.IsDonor ||
            message.EventType is LivestreamEventType.Gift or LivestreamEventType.Follow;
        if (Config.MessageRateLimitEnabled && !exemptFromRateLimit)
        {
            int windowSeconds = Config.MessageRateWindow == MessageRateWindow.OneSecond ? 1 : 10;
            RuleEngine.MessageRateResult rateResult = _ruleEngine.TryAcceptMessageRate(
                message.Author,
                windowSeconds,
                Math.Clamp(Config.PerUserMessageLimit, 1, 100),
                Math.Clamp(Config.StreamMessageLimit, 10, 5000),
                DateTimeOffset.UtcNow);
            if (rateResult != RuleEngine.MessageRateResult.Allowed)
            {
                bool perUser = rateResult == RuleEngine.MessageRateResult.UserLimitExceeded;
                return new ModerationDecision
                {
                    Message = message,
                    Disposition = ModerationDisposition.Rejected,
                    ReasonCode = perUser
                        ? ModerationReasonCode.UserCooldown
                        : ModerationReasonCode.SpamPattern,
                    ReasonDescription = perUser
                        ? $"Sender exceeded {Config.PerUserMessageLimit} messages per {windowSeconds} seconds"
                        : $"Stream exceeded {Config.StreamMessageLimit} messages per {windowSeconds} seconds",
                    SpokenText = string.Empty,
                    NormalizedText = message.RawText
                };
            }
        }

        // 4. Legacy user cooldown rule retained for API compatibility. The app
        // uses the adjustable sliding rate limit above instead.
        if (!exemptFromRateLimit && _ruleEngine.IsUserInCooldown(message.Author, Config.UserCooldownSeconds, DateTimeOffset.UtcNow))
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

        // 5. Chatter @Reply Filtering
        // When IgnoreChatReplies is true, messages that start with @recipient directed
        // at other chatters are ignored so viewer-to-viewer conversations are not spoken aloud.
        // Direct messages to the streamer (matching StreamerUsername) are not considered replies.
        if (Config.IgnoreChatReplies && !isSystemEvent && message.EventType == LivestreamEventType.Chat)
        {
            var replyMatch = ReplyPrefixRegex().Match(message.RawText);
            if (replyMatch.Success)
            {
                string handle = replyMatch.Groups["handle"].Value;
                string? streamerHandle = Config.StreamerUsername?.Trim().TrimStart('@');
                bool isDirectToStreamer = !string.IsNullOrWhiteSpace(streamerHandle) &&
                    string.Equals(handle.TrimStart('@'), streamerHandle, StringComparison.OrdinalIgnoreCase);

                if (!isDirectToStreamer)
                {
                    return new ModerationDecision
                    {
                        Message = message,
                        Disposition = ModerationDisposition.Rejected,
                        ReasonCode = ModerationReasonCode.ChatReply,
                        ReasonDescription = $"Message is an @reply to @{handle} and chatter-to-chatter replies are disabled",
                        SpokenText = string.Empty,
                        NormalizedText = message.RawText
                    };
                }
            }
        }

        // 6. Mention Resolution & Sanitization
        // Mentions are treated like author display names: if a mentioned name contains
        // mixed scripts, disallowed non-Latin characters, or toxic patterns, it is filtered
        // to "a player" so innocent chat messages are not rejected, provided the message is safe.
        var (sanitizedText, hadUnsafeMentions, matchedBlockedMention, nonMentionText) =
            await SanitizeMentionsAsync(message.RawText, cancellationToken);

        if (matchedBlockedMention)
        {
            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.BlockedTerm,
                ReasonDescription = "Matches prohibited term or pattern in mention",
                SpokenText = string.Empty,
                NormalizedText = message.RawText
            };
        }

        if (hadUnsafeMentions && string.IsNullOrWhiteSpace(nonMentionText))
        {
            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.SpamPattern,
                ReasonDescription = "Message contained only an invalid mention with no content",
                SpokenText = string.Empty,
                NormalizedText = message.RawText
            };
        }

        string textForInspection = sanitizedText;

        // 6. Multi-layer Deobfuscation for Security Inspection
        string normalizedForInspection = UnicodeNormalizer.NormalizeForInspection(textForInspection);
        string decomposedForScriptInspection = UnicodeNormalizer.RemoveDiacritics(
            UnicodeNormalizer.StripInvisibleCharacters(textForInspection));

        // 7. Script & Language Validation
        if (Config.RejectMixedScripts &&
            (ScriptValidator.ContainsMixedScriptWords(textForInspection) ||
             ScriptValidator.ContainsMixedScriptWords(decomposedForScriptInspection) ||
             ScriptValidator.ContainsMixedScriptWords(normalizedForInspection)))
        {
            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.DisallowedScript,
                ReasonDescription = "Mixed writing systems detected in single word (homoglyph spoofing attempt)",
                SpokenText = string.Empty,
                NormalizedText = normalizedForInspection
            };
        }

        if (Config.EnglishOnly &&
            (!ScriptValidator.IsLatinOrEmojiOnly(textForInspection) ||
             !ScriptValidator.IsLatinOrEmojiOnly(decomposedForScriptInspection) ||
             !ScriptValidator.IsLatinOrEmojiOnly(normalizedForInspection)))
        {
            return new ModerationDecision
            {
                Message = message,
                Disposition = ModerationDisposition.Rejected,
                ReasonCode = ModerationReasonCode.DisallowedScript,
                ReasonDescription = "Non-Latin script detected while English-only mode is active",
                SpokenText = string.Empty,
                NormalizedText = normalizedForInspection
            };
        }

        // 8. Blocklist / Prohibited Rule Matching
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
        // contextual hostility. System events with static phrases skip this step.
        IntentClassificationResult intentResult;
        if (isSystemEvent)
        {
            intentResult = new IntentClassificationResult
            {
                IsToxic = false,
                ToxicityScore = 0.0,
                ModelUsed = "System Event Fast-Path"
            };
        }
        else
        {
            try
            {
                intentResult = await _intentClassifier.ClassifyAsync(
                    normalizedForInspection,
                    cancellationToken);
                if (!HasValidIntentScores(intentResult))
                {
                    throw new InvalidDataException("The contextual safety layer returned invalid scores.");
                }

                intentResult = ContextualTargetingPolicy.Apply(
                    textForInspection,
                    normalizedForInspection,
                    intentResult);
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

        // 10. Prepare Cleaned Spoken Output
        string speechCleaned = UnicodeNormalizer.CleanForSpeech(textForInspection, Config.StripUrls);

        string safeDisplayName = await GetSafeDisplayNameAsync(
            message.AuthorDisplayName,
            cancellationToken);
        string platformName = SafePlatformName(message.Platform);
        string finalSpokenText = message.AttributionStyle switch
        {
            SpokenAttributionStyle.LeadingName => $"{safeDisplayName}, {speechCleaned}",
            SpokenAttributionStyle.SaysOnPlatform =>
                $"{safeDisplayName} on {platformName} said: {speechCleaned}",
            SpokenAttributionStyle.LeadingNameOnPlatform =>
                $"{safeDisplayName} on {platformName}, {speechCleaned}",
            _ => $"{safeDisplayName} says: {speechCleaned}"
        };

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

    private static string SafePlatformName(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return "the livestream";
        }

        string cleaned = UnicodeNormalizer.CleanForSpeech(platform, stripUrls: true).Trim();
        return cleaned.Length is > 0 and <= 40
            ? cleaned
            : "the livestream";
    }

    [GeneratedRegex(@"(?<=^|\s|[(\[{'""/])@(?<handle>[^\s]+)", RegexOptions.Compiled)]
    private static partial Regex MentionRegex();

    [GeneratedRegex(@"^\s*[.(\[{'""/]*@(?<handle>[^\s,:;!?)]+)", RegexOptions.Compiled)]
    private static partial Regex ReplyPrefixRegex();

    private async Task<(string SanitizedText, bool HadUnsafeMentions, bool MatchedBlockedTermInMention, string NonMentionText)> SanitizeMentionsAsync(
        string rawText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(rawText) || !rawText.Contains('@'))
        {
            return (rawText, false, false, rawText);
        }

        var matches = MentionRegex().Matches(rawText);
        if (matches.Count == 0)
        {
            return (rawText, false, false, rawText);
        }

        var sb = new StringBuilder(rawText.Length);
        var nonMentionSb = new StringBuilder(rawText.Length);
        int lastIndex = 0;
        bool hadUnsafe = false;
        bool matchedBlockedTerm = false;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                string before = rawText.Substring(lastIndex, match.Index - lastIndex);
                sb.Append(before);
                nonMentionSb.Append(before);
            }

            string rawHandle = match.Groups["handle"].Value;
            string trailingPunct = string.Empty;

            while (rawHandle.Length > 0 && ",:;!?\"')]}".Contains(rawHandle[^1]))
            {
                trailingPunct = rawHandle[^1] + trailingPunct;
                rawHandle = rawHandle[..^1];
            }

            if (rawHandle.EndsWith('.') && !rawHandle.Contains(".."))
            {
                if (rawHandle.Length <= 1 || !char.IsLetter(rawHandle[^2]) || (rawHandle.Length > 2 && rawHandle[^3] != '.'))
                {
                    trailingPunct = "." + trailingPunct;
                    rawHandle = rawHandle[..^1];
                }
            }

            if (string.IsNullOrWhiteSpace(rawHandle))
            {
                sb.Append(match.Value);
                nonMentionSb.Append(match.Value);
            }
            else
            {
                var (isSafe, safeName, matchedBlock) = await EvaluateNameSafetyWithBlockCheckAsync(
                    rawHandle,
                    "a player",
                    cancellationToken);

                if (matchedBlock)
                {
                    matchedBlockedTerm = true;
                }

                if (isSafe)
                {
                    sb.Append('@').Append(rawHandle).Append(trailingPunct);
                }
                else
                {
                    hadUnsafe = true;
                    sb.Append(safeName).Append(trailingPunct);
                }
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < rawText.Length)
        {
            string remainder = rawText.Substring(lastIndex);
            sb.Append(remainder);
            nonMentionSb.Append(remainder);
        }

        string sanitizedText = sb.ToString();
        string nonMentionText = nonMentionSb.ToString().Trim();

        return (sanitizedText, hadUnsafe, matchedBlockedTerm, nonMentionText);
    }

    private async Task<(bool IsSafe, string CleanedName, bool MatchedBlockedTerm)> EvaluateNameSafetyWithBlockCheckAsync(
        string? name,
        string fallbackPlaceholder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 50)
        {
            return (false, fallbackPlaceholder, false);
        }

        string normalized = UnicodeNormalizer.NormalizeForInspection(name);
        string decomposedForScriptInspection = UnicodeNormalizer.RemoveDiacritics(
            UnicodeNormalizer.StripInvisibleCharacters(name));

        bool matchedBlocked = _ruleEngine.MatchesBlockedTerms(
            normalized,
            Config.CustomBlockedTerms,
            Config.CustomAllowedTerms,
            out _);

        if (matchedBlocked)
        {
            return (false, fallbackPlaceholder, true);
        }

        if (ScriptValidator.ContainsMixedScriptWords(name) ||
            ScriptValidator.ContainsMixedScriptWords(decomposedForScriptInspection) ||
            ScriptValidator.ContainsMixedScriptWords(normalized) ||
            (Config.EnglishOnly &&
             (!ScriptValidator.IsLatinOrEmojiOnly(decomposedForScriptInspection) ||
              !ScriptValidator.IsLatinOrEmojiOnly(normalized))))
        {
            return (false, fallbackPlaceholder, false);
        }

        try
        {
            IntentClassificationResult nameIntent = await _intentClassifier.ClassifyAsync(
                normalized,
                cancellationToken);
            if (!HasValidIntentScores(nameIntent) ||
                nameIntent.ToxicityScore >= Config.IntentToxicityThreshold)
            {
                return (false, fallbackPlaceholder, false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (false, fallbackPlaceholder, false);
        }

        string cleaned = UnicodeNormalizer.CleanDisplayNameForSpeech(name);
        return string.IsNullOrWhiteSpace(cleaned)
            ? (false, fallbackPlaceholder, false)
            : (true, cleaned, false);
    }

    private async Task<string> GetSafeDisplayNameAsync(
        string? displayName,
        CancellationToken cancellationToken)
    {
        var (isSafe, cleaned, _) = await EvaluateNameSafetyWithBlockCheckAsync(
            displayName,
            "A viewer",
            cancellationToken);
        return isSafe ? cleaned : "A viewer";
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
