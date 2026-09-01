using System.Text.RegularExpressions;

namespace SafeSpeak.Core.AI;

/// <summary>
/// Fast heuristic intent and toxicity estimator with zero external model dependencies.
/// Uses sentiment markers, harassment patterns, threat signals, harm wishes, and malicious intimidation.
/// </summary>
public sealed partial class HeuristicIntentClassifier : IIntentClassifier
{
    public string ModelName => "FastHeuristicIntentEngine (Built-in)";
    public bool IsModelLoaded => true;

    [GeneratedRegex(@"\b(kill|hang|shoot|stab|murder|burn|beat)\s+(you|urself|yourself|him|her|them)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ThreatRegex();

    [GeneratedRegex(@"\b(?:watch\s+your\s+back|look\s+over\s+your\s+shoulder|(?:i|we)\s+know\s+where\s+(?:you|u)\s+(?:live|sleep|stream|work|stay)|(?:you\s+are|you're|ur|youre)\s+not\s+safe|(?:we|i|someone|people)\s+(?:are|is)?\s*(?:coming\s+(?:for|after|to\s+get)\s+(?:you|u)|coming\s+to\s+your\s+(?:house|home|door))|(?:better|you\s+better)\s+lock\s+your\s+(?:doors?|windows?)|(?:someone|somebody)\s+(?:is\s+going\s+to|is\s+gonna|will)\s+(?:get|hurt|find|jump|beat|attack|harm)\s+(?:you|u)|(?:your|ur)\s+days\s+are\s+numbered|wait\s+till\s+(?:i|we)\s+(?:find|catch|see)\s+(?:you|u)|you(?:'ll|\s+will)\s+pay\s+for\s+this)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VeiledThreatAndIntimidationRegex();

    [GeneratedRegex(@"\b(?:(?:i\s+)?(?:hope|wish|pray)\s+(?:that\s+)?(?:something|anything|everything)\s+(?:terrible|horrible|awful|bad|tragic|evil|harmful|catastrophic|painful)\s+(?:happens?|happened|occurs?|befalls?)\s+(?:to\s+)?(?:you|u|ur|your)|(?:i\s+)?(?:hope|wish)\s+(?:you|u)\s+(?:get\s+(?:hurt|sick|cancer|into\s+(?:an?\s+)?(?:car\s+|vehicle\s+|terrible\s+|bad\s+)?(?:accident|crash|wreck|collision)|ruined|fired|arrested|jumped|beaten|robbed|attacked|injured)|suffer|choke|drown|burn|bleed|die|starve|fail|lose\s+everything|rot|drop\s+dead|crash)|(?:you|u)\s+deserve\s+to\s+(?:suffer|die|burn|be\s+hurt|get\s+hurt|get\s+beaten|rot|lose\s+everything|be\s+punished|be\s+attacked|fail)|may\s+(?:you|u)\s+(?:rot|burn|suffer|die|choke|never\s+find\s+happiness)|hope\s+(?:your|ur)\s+(?:house\s+burns|family\s+gets\s+hurt|car\s+crashes|life\s+is\s+ruined))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaliciousHarmWishRegex();

    [GeneratedRegex(@"\b(?:the\s+world\s+(?:would|will)\s+be\s+better\s+off\s+without\s+(?:you|u)|(?:nobody|no\s+one|no1)\s+(?:would|will)?\s*(?:care|miss\s+you|notice|cry)\s+(?:if|when)\s+(?:you|u)\s+(?:die|died|were\s+dead|disappear|are\s+gone)|do\s+(?:us|everyone|the\s+world)\s+a\s+favor\s+and\s+(?:die|end\s+it|kill\s+yourself|disappear)|(?:drink\s+bleach|jump\s+off\s+a\s+(?:bridge|cliff|building)|step\s+in\s+front\s+of\s+a\s+train|take\s+a\s+toaster\s+bath|bath\s+with\s+a\s+toaster|slit\s+your\s+wrists)|end\s+your\s+(?:life|existence)|put\s+a\s+bullet\s+(?:in|through)\s+(?:your|ur)\s+(?:head|skull|brain)|take\s+your\s+own\s+life)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelfHarmEncouragementRegex();

    [GeneratedRegex(@"\b(?:waste\s+of\s+(?:oxygen|space|air|life|breath|skin|human\s+tissue)|(?:you\s+are|youre|you're|ur|u\s+are)\s+(?:worthless|useless|subhuman|human\s+garbage|human\s+trash|scum|a\s+parasite|a\s+plague|a\s+mistake)|(?:nobody|no\s+one|no1)\s+(?:loves|likes|wants|cares\s+about)\s+(?:you|u)|(?:you|u)\s+(?:don't|dont)\s+deserve\s+to\s+(?:live|exist|breathe|be\s+happy|be\s+alive)|(?:you|u)\s+should\s+(?:be\s+ashamed\s+of\s+(?:existing|being\s+alive)|never\s+have\s+been\s+born))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TargetedDegradationRegex();

    [GeneratedRegex(@"\b(?:(?:i|we)\s+(?:have|got)\s+your\s+(?:ip\s+address|real\s+address|home\s+address|phone\s+number|dox)|swatting\s+(?:you|u)|swat\s+team\s+is\s+coming|calling\s+the\s+(?:cops|police)\s+on\s+your\s+(?:house|stream)|(?:leaking|posting)\s+your\s+(?:dox|address|ip|location))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DoxxSwatThreatRegex();

    [GeneratedRegex(@"\b(?:i\s+)?hope\s+(?:you|u)\s+(?:fail|get\s+banned|get\s+deleted|lose\s+all\s+(?:your|ur)\s+viewers|fall\s+off)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpitefulWishRegex();

    [GeneratedRegex(@"\b(trash|ugly|loser|fat|disgusting|pathetic|idiot|moron|dumb|clown|stupid)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InsultRegex();

    [GeneratedRegex(@"\b(you\s+are|youre|you're|ur|u\s+r)\s+(trash|ugly|a\s+loser|fat|disgusting|pathetic|an?\s+idiot|a\s+moron|dumb|a\s+clown|stupid)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DirectedInsultRegex();

    [GeneratedRegex(@"\b(nobody\s+likes\s+you|waste\s+of\s+space|get\s+cancer|go\s+away\s+and\s+die)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HarassmentRegex();

    [GeneratedRegex(@"\b(shut\s+up|get\s+lost|go\s+away|no\s+one\s+asked|stop\s+talking)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostileDismissalRegex();

    [GeneratedRegex(@"\b(?:fuck(?:ing)?\s+(?:off|you|yourself|this|that)|go\s+fuck\s+yourself|piss\s+off|screw\s+you)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostileProfanityRegex();

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
        double severeToxicityScore = 0.0;
        string category = "None";

        if (SelfHarmEncouragementRegex().IsMatch(text))
        {
            severeToxicityScore = 0.98;
            threatScore = Math.Max(threatScore, 0.95);
            harassmentScore = Math.Max(harassmentScore, 0.98);
            category = "Self-harm encouragement";
        }

        if (ThreatRegex().IsMatch(text) ||
            VeiledThreatAndIntimidationRegex().IsMatch(text) ||
            DoxxSwatThreatRegex().IsMatch(text))
        {
            threatScore = Math.Max(threatScore, 0.95);
            harassmentScore = Math.Max(harassmentScore, 0.90);
            if (category == "None") category = "Threat or intimidation";
        }

        if (MaliciousHarmWishRegex().IsMatch(text))
        {
            threatScore = Math.Max(threatScore, 0.85);
            harassmentScore = Math.Max(harassmentScore, 0.90);
            if (category == "None") category = "Hostile harm wish";
        }

        if (TargetedDegradationRegex().IsMatch(text))
        {
            harassmentScore = Math.Max(harassmentScore, 0.90);
            insultScore = Math.Max(insultScore, 0.85);
            if (category == "None") category = "Targeted degradation";
        }

        if (HarassmentRegex().IsMatch(text))
        {
            harassmentScore = Math.Max(harassmentScore, 0.85);
            if (category == "None") category = "Harassment";
        }

        if (HostileProfanityRegex().IsMatch(text))
        {
            insultScore = Math.Max(insultScore, 0.82);
            harassmentScore = Math.Max(harassmentScore, 0.75);
            if (category == "None") category = "Hostile profanity";
        }

        if (SpitefulWishRegex().IsMatch(text))
        {
            harassmentScore = Math.Max(harassmentScore, 0.70);
            insultScore = Math.Max(insultScore, 0.65);
            if (category == "None") category = "Hostile spite";
        }

        if (DirectedInsultRegex().IsMatch(text))
        {
            insultScore = Math.Max(insultScore, 0.70);
            if (category == "None") category = "Directed insult";
        }
        else
        {
            var insultMatches = InsultRegex().Matches(text);
            if (insultMatches.Count > 0)
            {
                double score = insultMatches.Count > 1 ? 0.64 : 0.48;
                insultScore = Math.Max(insultScore, score);
                if (category == "None") category = "Insult";
            }
        }

        if (HostileDismissalRegex().IsMatch(text))
        {
            insultScore = Math.Max(insultScore, 0.52);
            if (category == "None") category = "Hostile dismissal";
        }

        double totalToxicity = Math.Max(severeToxicityScore,
            Math.Max(threatScore, Math.Max(harassmentScore, insultScore)));

        if (category == "None")
        {
            if (threatScore >= 0.8) category = "Threat";
            else if (harassmentScore >= 0.7) category = "Harassment";
            else if (insultScore >= 0.6) category = "Directed insult";
            else if (insultScore > 0) category = "Hostile language";
        }

        var result = new IntentClassificationResult
        {
            IsToxic = totalToxicity >= 0.6,
            ToxicityScore = totalToxicity,
            SevereToxicityScore = severeToxicityScore,
            ThreatScore = threatScore,
            HarassmentScore = harassmentScore,
            InsultScore = insultScore,
            FlaggedCategory = category,
            ModelUsed = ModelName
        };

        return Task.FromResult(result);
    }

    public void Dispose() { }
}
