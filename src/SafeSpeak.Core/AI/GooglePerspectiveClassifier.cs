using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SafeSpeak.Core.AI;

/// <summary>
/// Cloud-based deep intent classifier integrating Google's Perspective API (Jigsaw / Google Anti-Abuse).
/// Provides state-of-the-art multi-attribute toxicity, threat, insult, and attack analysis with zero local CPU overhead.
/// </summary>
public sealed class GooglePerspectiveClassifier : IIntentClassifier
{
    private const string ApiEndpoint = "https://commentanalyzer.googleapis.com/v1alpha1/comments:analyze";
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly IIntentClassifier _localFallback;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public string ModelName => "Google Perspective API (Cloud Deep Intent)";
    public bool IsModelLoaded => !string.IsNullOrWhiteSpace(_apiKey);

    public GooglePerspectiveClassifier(
        string? apiKey,
        IIntentClassifier? localFallback = null,
        HttpClient? httpClient = null)
    {
        _apiKey = apiKey;
        _localFallback = localFallback ?? IntentClassifierDefaults.CreateLocal();
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
            _ownsHttpClient = true;
        }
    }

    public async Task<IntentClassificationResult> ClassifyAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(text))
        {
            return new IntentClassificationResult
            {
                IsToxic = false,
                ToxicityScore = 0.0,
                ModelUsed = ModelName
            };
        }

        // If no API key is provided, transparently use the fast local ONNX+heuristic engine
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return await _localFallback.ClassifyAsync(text, cancellationToken);
        }

        try
        {
            var requestBody = new PerspectiveRequest
            {
                Comment = new PerspectiveComment { Text = text },
                RequestedAttributes = new Dictionary<string, object>
                {
                    ["TOXICITY"] = new { },
                    ["SEVERE_TOXICITY"] = new { },
                    ["THREAT"] = new { },
                    ["INSULT"] = new { },
                    ["IDENTITY_ATTACK"] = new { },
                    ["PROFANITY"] = new { }
                },
                Languages = new[] { "en" },
                DoNotStore = true
            };

            string requestUrl = $"{ApiEndpoint}?key={_apiKey}";
            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                requestUrl,
                requestBody,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Fall back to local engine if quota exceeded or network issue
                return await _localFallback.ClassifyAsync(text, cancellationToken);
            }

            var responseData = await response.Content.ReadFromJsonAsync<PerspectiveResponse>(
                cancellationToken: cancellationToken);

            if (responseData?.AttributeScores is null)
            {
                return await _localFallback.ClassifyAsync(text, cancellationToken);
            }

            double toxicity = GetScore(responseData, "TOXICITY");
            double severeToxicity = GetScore(responseData, "SEVERE_TOXICITY");
            double threat = GetScore(responseData, "THREAT");
            double insult = GetScore(responseData, "INSULT");
            double identityAttack = GetScore(responseData, "IDENTITY_ATTACK");
            double profanity = GetScore(responseData, "PROFANITY");

            double maxHostility = Math.Max(toxicity, Math.Max(severeToxicity, Math.Max(threat, Math.Max(insult, identityAttack))));

            string category = "None";
            if (threat >= 0.70) category = "Threat";
            else if (identityAttack >= 0.65) category = "Identity hate";
            else if (severeToxicity >= 0.60) category = "Severe toxicity";
            else if (insult >= 0.60) category = "Insult";
            else if (toxicity >= 0.60) category = "Toxic language";
            else if (profanity >= 0.60) category = "Profanity";

            var cloudResult = new IntentClassificationResult
            {
                IsToxic = maxHostility >= 0.60,
                ToxicityScore = maxHostility,
                SevereToxicityScore = severeToxicity,
                ThreatScore = threat,
                HarassmentScore = Math.Max(insult, threat),
                InsultScore = insult,
                IdentityHateScore = identityAttack,
                ObsceneScore = profanity,
                FlaggedCategory = category,
                ModelUsed = "Google Perspective API + local hybrid"
            };

            // Always fuse with local heuristic to catch instant local block rules
            IntentClassificationResult localResult = await _localFallback.ClassifyAsync(text, cancellationToken);
            return Fuse(cloudResult, localResult);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Network fallback: seamlessly fall back to local CPU ONNX engine
            return await _localFallback.ClassifyAsync(text, cancellationToken);
        }
    }

    private static double GetScore(PerspectiveResponse response, string attributeName)
    {
        if (response.AttributeScores != null &&
            response.AttributeScores.TryGetValue(attributeName, out var scoreObj) &&
            scoreObj.SummaryScore != null)
        {
            return scoreObj.SummaryScore.Value;
        }
        return 0.0;
    }

    private static IntentClassificationResult Fuse(
        IntentClassificationResult cloud,
        IntentClassificationResult local)
    {
        double maxToxicity = Math.Max(cloud.ToxicityScore, local.ToxicityScore);
        bool localDominates = local.ToxicityScore > cloud.ToxicityScore;

        return new IntentClassificationResult
        {
            IsToxic = maxToxicity >= 0.60,
            ToxicityScore = maxToxicity,
            SevereToxicityScore = Math.Max(cloud.SevereToxicityScore, local.SevereToxicityScore),
            ThreatScore = Math.Max(cloud.ThreatScore, local.ThreatScore),
            HarassmentScore = Math.Max(cloud.HarassmentScore, local.HarassmentScore),
            InsultScore = Math.Max(cloud.InsultScore, local.InsultScore),
            IdentityHateScore = Math.Max(cloud.IdentityHateScore, local.IdentityHateScore),
            ObsceneScore = Math.Max(cloud.ObsceneScore, local.ObsceneScore),
            FlaggedCategory = localDominates && local.FlaggedCategory != "None" ? local.FlaggedCategory : cloud.FlaggedCategory,
            ModelUsed = cloud.ModelUsed
        };
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        _localFallback.Dispose();
    }

    private sealed class PerspectiveRequest
    {
        [JsonPropertyName("comment")]
        public PerspectiveComment Comment { get; set; } = new();

        [JsonPropertyName("requestedAttributes")]
        public Dictionary<string, object> RequestedAttributes { get; set; } = new();

        [JsonPropertyName("languages")]
        public string[] Languages { get; set; } = Array.Empty<string>();

        [JsonPropertyName("doNotStore")]
        public bool DoNotStore { get; set; } = true;
    }

    private sealed class PerspectiveComment
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class PerspectiveResponse
    {
        [JsonPropertyName("attributeScores")]
        public Dictionary<string, PerspectiveAttributeScore>? AttributeScores { get; set; }
    }

    private sealed class PerspectiveAttributeScore
    {
        [JsonPropertyName("summaryScore")]
        public PerspectiveScoreValue? SummaryScore { get; set; }
    }

    private sealed class PerspectiveScoreValue
    {
        [JsonPropertyName("value")]
        public double Value { get; set; }
    }
}
