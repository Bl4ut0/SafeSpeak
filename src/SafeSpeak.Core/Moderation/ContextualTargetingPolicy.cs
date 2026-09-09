using System.Text.RegularExpressions;
using SafeSpeak.Core.AI;

namespace SafeSpeak.Core.Moderation;

/// <summary>
/// Calibrates single-message intent scores using explicit target, speech-act,
/// severity, and ambiguity signals. The score anchors align with the four
/// user-facing moderation thresholds while retaining a narrow hard-safety
/// floor for explicit sexual exploitation of minors.
/// </summary>
internal static partial class ContextualTargetingPolicy
{
    private const double ExplicitMinorSexualIntentScore = 0.99;
    private const double ExplicitGenocidalIntentScore = 0.99;
    private const double DirectedHostilityScore = 0.85;
    private const double AmbiguousMinorSafetyScore = 0.70;
    private const double SafeContextCeiling = 0.40;

    [GeneratedRegex(
        @"\b(?:(?:i(?:['’]m)?|we)\s+(?:(?:really\s+)?(?:want|plan|intend|hope|would\s+like|can['’]?t\s+wait|am\s+excited|are\s+excited|excited)\s+to\s+)?(?:have\s+sex|sleep|hook\s+up|make\s+out|do\s+sexual\s+(?:things|stuff)|touch|fondle|molest|rape)\s+with\s+(?:(?:my|our|his|her|their|the|an?)\s+)?(?:(?:underage|young|little)\s+)?(?:child|kid|minor|girl|boy|person|niece|nephew|daughter|son|\d{1,2}(?:[- ]year[- ]old)?)|(?:this|that|the|my|our|his|her|their)\s+(?:child|kid|minor|underage\s+(?:girl|boy|person)|young\s+(?:girl|boy)|little\s+(?:girl|boy)|niece|nephew|daughter|son)\s+(?:is|looks?|seems?)\s+(?:hot|sexy|attractive)|(?:send|show|give)\s+me\s+(?:your\s+)?(?:nudes?|(?:nude|naked)\s+(?:pictures?|photos?))\s*,?\s*(?:child|kid|minor|underage\s+(?:girl|boy|person)|young\s+(?:girl|boy)|little\s+(?:girl|boy))|can['’]?t\s+wait\s+until\s+(?:(?:my|our|his|her|their|the)\s+)?(?:child|kid|niece|nephew|daughter|son|\d{1,2}(?:[- ]year[- ]old)?)\s+is\s+legal)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitMinorSexualIntentRegex();

    [GeneratedRegex(
        @"^\s*(?:(?:i(?:['’]m)?|we|they|he|she)\s+(?:(?:am|are|is|was|were)\s+)?(?:really\s+)?(?:excited|looking\s+forward|eager|can['’]?t\s+wait)\s+to\s+(?:do|try)\s+(?:(?:some|those|certain|private)\s+)?things\s+with\s+(?:(?:my|our|his|her|their|the)\s+)?(?:child|kid|minor|niece|nephew|daughter|son)|(?:(?:my|our|his|her|their|the)\s+)?(?:child|kid|minor|niece|nephew|daughter|son)\s+and\s+(?:i|we|they|he|she)\s+(?:have|share|keep)\s+(?:a|our)\s+(?:special|private)\s+secret(?:\s+(?:that\s+)?(?:adults?|parents?|family)\s+(?:can['’]?t|mustn['’]?t|shouldn['’]?t)\s+know)?)\s*[.!?]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmbiguousMinorSafetyRegex();

    [GeneratedRegex(
        @"^\s*(?:(?:(?:child(?:ren)?(?:['’]s)?\s+sexual\s+(?:abuse|exploitation)|sexual\s+(?:abuse|exploitation)\s+of\s+(?:a\s+)?child(?:ren)?)\s+(?:is|are)\s+(?:wrong|harmful|illegal|unacceptable|never\s+acceptable))|(?:(?:we\s+(?:must|should)|please)\s+)?protect\s+(?:kids|children|minors)\s+from\s+(?:sexual\s+)?(?:abuse|exploitation|grooming)|(?:i|we)\s+(?:reported|am\s+reporting|are\s+reporting)\s+(?:suspected\s+)?(?:child\s+)?(?:sexual\s+)?(?:abuse|exploitation|grooming)\s+to\s+(?:the\s+)?(?:authorities|police|moderators?))\s*[.!?]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProtectiveMinorSafetyRegex();

    [GeneratedRegex(
        @"\b(?:(?:i|we)\s+(?:support|endorse|celebrate|welcome|want|demand|plan|intend|will|would\s+support)\s+(?:a\s+|the\s+)?genocide(?!\s+(?:prevention|education|awareness|research|remembrance|survivors?|victims?|stopped|prevented|ended|to\s+(?:stop|end|be\s+stopped|be\s+prevented)))|(?:you|u|they|them|those\s+people|your\s+people|that\s+group)\s+(?:deserve|need|should\s+(?:face|suffer|experience))\s+(?:a\s+|the\s+)?genocide|(?:we|you|they)\s+(?:should|must|need\s+to|have\s+to|will)\s+(?:commit|carry\s+out|start|finish|complete)\s+(?:a\s+|the\s+)?genocide|(?:i|we|you|they)\s+(?:should|must|need\s+to|have\s+to|will|want\s+to|plan\s+to|intend\s+to)\s+(?:genocide|exterminate|eradicate)\s+(?:them|you|u|those\s+people|your\s+people|that\s+group)(?:\s+all)?|(?:them|you|u|those\s+people|your\s+people|that\s+group)\s+(?:should|must|need\s+to)\s+be\s+(?:genocided|exterminated|eradicated)|(?:commit|carry\s+out|start|finish|complete)\s+(?:a\s+|the\s+)?genocide\s+(?:against|of)\b|(?:genocide|exterminate|eradicate)\s+(?:them|you|u|those\s+people|your\s+people|that\s+group)(?:\s+all)?|(?:i|we)\s+(?:hope|wish|pray)\s+(?:you|u|they|your\s+people|those\s+people)\s+(?:face|suffer|experience|get)\s+(?:a\s+|the\s+)?genocide)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitGenocidalIntentRegex();

    [GeneratedRegex(
        @"^\s*(?:genocide\s+(?:is|was)\s+(?:a\s+)?(?:crime(?:\s+against\s+humanity)?|wrong|evil|horrific|an?\s+atrocity|never\s+acceptable)|(?:(?:we\s+)?(?:must|should)\s+|please\s+)?(?:prevent|stop|oppose|condemn)\s+genocide|(?:i|we|they|students?)\s+(?:am|are|were)?\s*(?:studying|learning|reading|talking|teaching)\s+about\s+(?:the\s+)?genocide|(?:remember|honor)\s+(?:the\s+)?(?:victims?|survivors?)\s+of\s+genocide|genocide\s+(?:prevention|education|awareness|research|remembrance)\s+(?:is|remains)\s+(?:important|necessary|essential)|(?:this|that|the)\s+(?:book|article|documentary|lesson|museum|class)\s+(?:is|was)\s+about\s+genocide)\s*[.!?]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProtectiveOrInformationalGenocideRegex();

    [GeneratedRegex(
        @"\b(?:(?:i|we)\s+(?:really\s+|absolutely\s+)?(?:hate|despise|can['’]?t\s+stand)\s+(?:you|u|him|her|them|@[\p{L}\p{N}_]{1,32}|your\s+(?:stream|channel|content)|(?:this|that|the)\s+(?:stream|streamer|creator|host|channel|chat|community)|(?:the\s+)?(?:streamer|creator|host)|(?:all\s+)?(?:people|viewers|streamers|moderators)|everyone|(?:everyone|people|viewers)\s+(?:here|in\s+(?:this\s+)?(?:chat|stream)))|(?:this|that|the)\s+(?:streamer|creator|host)\s+(?:is|was)\s+(?:trash|garbage|awful|stupid|an?\s+idiot|a\s+moron|pathetic|disgusting|terrible|the\s+worst)|(?:everyone|people|viewers)\s+(?:here|in\s+(?:this\s+)?(?:chat|stream))\s+(?:is|are)\s+(?:trash|garbage|awful|stupid|pathetic|disgusting|terrible|the\s+worst))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DirectedHostilityRegex();

    [GeneratedRegex(
        @"^\s*(?:(?:i|we)\s+(?:really\s+|absolutely\s+)?(?:hate|despise|can['’]?t\s+stand)\s+(?:(?:this|that|the)\s+)?(?:game|level|boss|mechanic|map|quest|match|round|update|patch|controls?|menu|soundtrack|gameplay|mode|puzzle)|(?:this|that|the)\s+(?:game|level|boss|mechanic|map|quest|match|round|update|patch|controls?|menu|soundtrack|gameplay|mode|puzzle)\s+(?:is|was|feels?|seems?)\s+(?:trash|garbage|awful|stupid|dumb|pathetic|disgusting|terrible|the\s+worst|frustrating|broken|bad|boring|annoying|unfair|sucks?)|(?:i|we)\s+hate\s+(?:losing|playing)(?:\s+(?:this|that|the))?\s+(?:game|match|round|level))\s*[.!?]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonHumanFrustrationRegex();

    public static IntentClassificationResult Apply(
        string originalText,
        string normalizedText,
        IntentClassificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(originalText) && string.IsNullOrWhiteSpace(normalizedText))
        {
            return result;
        }

        bool isExplicitMinorSexualIntent = ExplicitMinorSexualIntentRegex().IsMatch(originalText) ||
            ExplicitMinorSexualIntentRegex().IsMatch(normalizedText);
        if (isExplicitMinorSexualIntent)
        {
            return result with
            {
                IsToxic = true,
                ToxicityScore = Math.Max(result.ToxicityScore, ExplicitMinorSexualIntentScore),
                SevereToxicityScore = Math.Max(result.SevereToxicityScore, ExplicitMinorSexualIntentScore),
                HarassmentScore = Math.Max(result.HarassmentScore, 0.95),
                FlaggedCategory = "Explicit minor sexual exploitation",
                ModelUsed = $"{result.ModelUsed} + target-context policy"
            };
        }

        bool isExplicitGenocidalIntent = ExplicitGenocidalIntentRegex().IsMatch(originalText) ||
            ExplicitGenocidalIntentRegex().IsMatch(normalizedText);
        if (isExplicitGenocidalIntent)
        {
            return result with
            {
                IsToxic = true,
                ToxicityScore = Math.Max(result.ToxicityScore, ExplicitGenocidalIntentScore),
                SevereToxicityScore = Math.Max(result.SevereToxicityScore, ExplicitGenocidalIntentScore),
                ThreatScore = Math.Max(result.ThreatScore, 0.95),
                HarassmentScore = Math.Max(result.HarassmentScore, 0.95),
                FlaggedCategory = "Genocidal advocacy or threat",
                ModelUsed = $"{result.ModelUsed} + target-context policy"
            };
        }

        // Discussion, education, remembrance, and explicit opposition are not
        // endorsements of mass violence. Full-message matching prevents a safe
        // preface from concealing an appended threat or expression of support.
        bool isProtectiveOrInformationalGenocideStatement =
            ProtectiveOrInformationalGenocideRegex().IsMatch(originalText) ||
            ProtectiveOrInformationalGenocideRegex().IsMatch(normalizedText);
        if (isProtectiveOrInformationalGenocideStatement)
        {
            return ApplySafeContextCeiling(result, "Protective or informational genocide discussion");
        }

        // A complete protective statement is a speech act opposing harm, not
        // endorsing it. Full-message matching prevents a safe preface from
        // concealing an appended abusive clause.
        bool isProtectiveMinorSafetyStatement = ProtectiveMinorSafetyRegex().IsMatch(originalText) ||
            ProtectiveMinorSafetyRegex().IsMatch(normalizedText);
        if (isProtectiveMinorSafetyStatement)
        {
            return ApplySafeContextCeiling(result, "Protective child-safety statement");
        }

        // With no conversation history, vague euphemisms cannot establish a
        // crime or a person's age. They receive a 0.70 risk anchor: permissive
        // levels 1-2 allow them, while Strong and Maximum prevent narration.
        bool isAmbiguousMinorSafetyConcern = AmbiguousMinorSafetyRegex().IsMatch(originalText) ||
            AmbiguousMinorSafetyRegex().IsMatch(normalizedText);
        if (isAmbiguousMinorSafetyConcern)
        {
            return result with
            {
                IsToxic = true,
                ToxicityScore = AmbiguousMinorSafetyScore,
                SevereToxicityScore = Math.Max(result.SevereToxicityScore, 0.60),
                HarassmentScore = Math.Max(result.HarassmentScore, 0.65),
                FlaggedCategory = "Ambiguous child-safety concern",
                ModelUsed = $"{result.ModelUsed} + target-context policy"
            };
        }

        bool isDirectedHostility = DirectedHostilityRegex().IsMatch(originalText) ||
            DirectedHostilityRegex().IsMatch(normalizedText);
        if (isDirectedHostility)
        {
            bool hasIndependentSevereSignal = result.ThreatScore >= 0.90 ||
                result.SevereToxicityScore >= 0.90 ||
                result.IdentityHateScore >= 0.90;
            return result with
            {
                IsToxic = true,
                ToxicityScore = hasIndependentSevereSignal
                    ? Math.Max(result.ToxicityScore, DirectedHostilityScore)
                    : DirectedHostilityScore,
                HarassmentScore = Math.Max(result.HarassmentScore, 0.80),
                InsultScore = Math.Max(result.InsultScore, 0.80),
                FlaggedCategory = "Directed hostility",
                ModelUsed = $"{result.ModelUsed} + target-context policy"
            };
        }

        bool isNonHumanFrustration = NonHumanFrustrationRegex().IsMatch(originalText) ||
            NonHumanFrustrationRegex().IsMatch(normalizedText);
        if (!isNonHumanFrustration ||
            result.ThreatScore >= 0.75 ||
            result.SevereToxicityScore >= 0.75 ||
            result.IdentityHateScore >= 0.45)
        {
            return result;
        }

        return ApplySafeContextCeiling(result, "Non-directed game frustration");
    }

    private static IntentClassificationResult ApplySafeContextCeiling(
        IntentClassificationResult result,
        string category) =>
        result with
        {
            IsToxic = false,
            ToxicityScore = Math.Min(result.ToxicityScore, SafeContextCeiling),
            SevereToxicityScore = Math.Min(result.SevereToxicityScore, SafeContextCeiling),
            ObsceneScore = Math.Min(result.ObsceneScore, SafeContextCeiling),
            ThreatScore = Math.Min(result.ThreatScore, SafeContextCeiling),
            HarassmentScore = Math.Min(result.HarassmentScore, SafeContextCeiling),
            InsultScore = Math.Min(result.InsultScore, SafeContextCeiling),
            IdentityHateScore = Math.Min(result.IdentityHateScore, SafeContextCeiling),
            FlaggedCategory = category,
            ModelUsed = $"{result.ModelUsed} + target-context policy"
        };
}
