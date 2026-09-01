using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Moderation;

/// <summary>
/// High-speed deterministic rule evaluation engine for blocklists, audience criteria, and rate limiting.
/// </summary>
public sealed class RuleEngine
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _userLastMessageTimes = new();

    // Core default prohibited terms (stored normalized)
    private static readonly HashSet<string> DefaultProhibitedTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        // Severe hate speech, slurs, and malicious exploits
        "nigger", "nigga", "faggot", "fag", "kike", "chink", "spic", "wetback", "retard", "cunt",
        "kys", "kill yourself", "kill urself", "hang yourself", "die in a fire", "hope you die",
        "suicide", "childporn", "pedo", "pedophile", "rape", "rapist"
    };

    public IReadOnlySet<string> DefaultRules => DefaultProhibitedTerms;

    /// <summary>
    /// Checks if a user has exceeded the cooldown window.
    /// </summary>
    public bool IsUserInCooldown(string author, int cooldownSeconds, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(author) || cooldownSeconds <= 0) return false;

        if (_userLastMessageTimes.TryGetValue(author, out var lastTime))
        {
            if ((nowUtc - lastTime).TotalSeconds < cooldownSeconds)
            {
                return true;
            }
        }

        _userLastMessageTimes[author] = nowUtc;
        return false;
    }

    /// <summary>
    /// Checks audience tier eligibility.
    /// </summary>
    public bool IsAudienceEligible(
        ChatMessage message,
        AudienceMode mode,
        bool allowDonorsToSpeak = true)
    {
        if (allowDonorsToSpeak && message.IsDonor)
        {
            return true;
        }

        return mode switch
        {
            AudienceMode.All => true,
            AudienceMode.FollowersOnly => message.AuthorTier >= AuthorTier.Follower,
            AudienceMode.SubscribersOnly => message.AuthorTier >= AuthorTier.Subscriber,
            AudienceMode.ModeratorsOnly => message.AuthorTier >= AuthorTier.Moderator,
            _ => true
        };
    }

    /// <summary>
    /// Matches normalized text against default and custom blocklists.
    /// </summary>
    public bool MatchesBlockedTerms(
        string normalizedText,
        IEnumerable<string> customBlockedTerms,
        IEnumerable<string> customAllowedTerms,
        out string matchedTerm)
    {
        matchedTerm = string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedText)) return false;

        // Built-in severe-abuse rules are not overridable by user allow-list entries.
        foreach (var prohibited in DefaultProhibitedTerms)
        {
            if (ContainsTerm(normalizedText, prohibited))
            {
                matchedTerm = prohibited;
                return true;
            }
        }

        HashSet<string> normalizedAllowedTerms = customAllowedTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(UnicodeNormalizer.NormalizeForInspection)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Check custom blocked terms
        foreach (var blocked in customBlockedTerms)
        {
            if (string.IsNullOrWhiteSpace(blocked)) continue;
            string normBlocked = UnicodeNormalizer.NormalizeForInspection(blocked);
            if (normalizedAllowedTerms.Contains(normBlocked)) continue;
            if (ContainsTerm(normalizedText, normBlocked))
            {
                matchedTerm = blocked;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsTerm(string normalizedText, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return false;
        return Regex.IsMatch(
            normalizedText,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Clears user rate limit history (e.g. on stream restart).
    /// </summary>
    public void ResetCooldowns()
    {
        _userLastMessageTimes.Clear();
    }
}
