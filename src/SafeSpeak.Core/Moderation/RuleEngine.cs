using System.Collections.Concurrent;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Moderation;

/// <summary>
/// High-speed deterministic rule evaluation engine for blocklists, audience criteria, and rate limiting.
/// </summary>
public sealed class RuleEngine
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _userLastMessageTimes = new();
    private readonly object _messageRateLock = new();
    private readonly Queue<DateTimeOffset> _streamMessageTimes = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _userMessageTimes =
        new(StringComparer.OrdinalIgnoreCase);
    private int _rateLimitSweepCounter;

    public enum MessageRateResult
    {
        Allowed = 0,
        UserLimitExceeded = 1,
        StreamLimitExceeded = 2
    }

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
    /// Atomically applies sliding-window limits to one viewer and the whole
    /// stream. Rejected attempts are not retained, keeping state bounded by the
    /// configured accepted-message limits.
    /// </summary>
    public MessageRateResult TryAcceptMessageRate(
        string author,
        int windowSeconds,
        int perUserLimit,
        int streamLimit,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(author) ||
            windowSeconds <= 0 ||
            perUserLimit <= 0 ||
            streamLimit <= 0)
        {
            return MessageRateResult.Allowed;
        }

        DateTimeOffset cutoff = nowUtc.AddSeconds(-windowSeconds);
        lock (_messageRateLock)
        {
            Prune(_streamMessageTimes, cutoff);
            if (_streamMessageTimes.Count >= streamLimit)
            {
                return MessageRateResult.StreamLimitExceeded;
            }

            if (!_userMessageTimes.TryGetValue(author, out Queue<DateTimeOffset>? userTimes))
            {
                userTimes = new Queue<DateTimeOffset>();
                _userMessageTimes[author] = userTimes;
            }

            Prune(userTimes, cutoff);
            if (userTimes.Count >= perUserLimit)
            {
                return MessageRateResult.UserLimitExceeded;
            }

            userTimes.Enqueue(nowUtc);
            _streamMessageTimes.Enqueue(nowUtc);
            _rateLimitSweepCounter++;
            if (_rateLimitSweepCounter >= 256)
            {
                SweepInactiveUsers(cutoff);
                _rateLimitSweepCounter = 0;
            }
            return MessageRateResult.Allowed;
        }
    }

    private static void Prune(Queue<DateTimeOffset> timestamps, DateTimeOffset cutoff)
    {
        while (timestamps.TryPeek(out DateTimeOffset timestamp) && timestamp <= cutoff)
        {
            timestamps.Dequeue();
        }
    }

    private void SweepInactiveUsers(DateTimeOffset cutoff)
    {
        foreach ((string author, Queue<DateTimeOffset> timestamps) in _userMessageTimes.ToArray())
        {
            Prune(timestamps, cutoff);
            if (timestamps.Count == 0)
            {
                _userMessageTimes.Remove(author);
            }
        }
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

        int searchStart = 0;
        while (searchStart <= normalizedText.Length - term.Length)
        {
            int match = normalizedText.IndexOf(
                term,
                searchStart,
                StringComparison.OrdinalIgnoreCase);
            if (match < 0)
            {
                return false;
            }

            int after = match + term.Length;
            bool startsAtBoundary = match == 0 ||
                !char.IsLetterOrDigit(normalizedText[match - 1]);
            bool endsAtBoundary = after == normalizedText.Length ||
                !char.IsLetterOrDigit(normalizedText[after]);
            if (startsAtBoundary && endsAtBoundary)
            {
                return true;
            }

            searchStart = match + 1;
        }

        return false;
    }

    /// <summary>
    /// Clears user rate limit history (e.g. on stream restart).
    /// </summary>
    public void ResetCooldowns()
    {
        _userLastMessageTimes.Clear();
        lock (_messageRateLock)
        {
            _streamMessageTimes.Clear();
            _userMessageTimes.Clear();
            _rateLimitSweepCounter = 0;
        }
    }
}
