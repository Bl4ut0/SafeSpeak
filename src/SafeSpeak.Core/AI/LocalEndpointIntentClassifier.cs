using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SafeSpeak.Core.AI;

/// <summary>
/// Connects to a locally running LLM / SLM instance (such as Ollama, LM Studio, or LocalAI on localhost)
/// to provide full pragmatic intent reasoning entirely on the user's machine with zero external cloud dependencies.
/// </summary>
public sealed class LocalEndpointIntentClassifier : IIntentClassifier
{
    private readonly HttpClient _httpClient;
    private readonly string _endpointUrl;
    private readonly string _modelName;
    private readonly IIntentClassifier _fallback;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public string ModelName => $"Local LLM ({_modelName})";
    public bool IsModelLoaded => !string.IsNullOrWhiteSpace(_endpointUrl);

    public LocalEndpointIntentClassifier(
        string? endpointUrl = "http://localhost:11434",
        string? modelName = "llama3.2:1b",
        IIntentClassifier? fallback = null,
        HttpClient? httpClient = null)
    {
        string requestedEndpoint = string.IsNullOrWhiteSpace(endpointUrl)
            ? "http://localhost:11434"
            : endpointUrl.TrimEnd('/');
        if (!Uri.TryCreate(requestedEndpoint, UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme is not ("http" or "https") ||
            !endpoint.IsLoopback)
        {
            throw new ArgumentException(
                "The local intent endpoint must be an HTTP or HTTPS loopback address.",
                nameof(endpointUrl));
        }

        _endpointUrl = endpoint.AbsoluteUri.TrimEnd('/');
        _modelName = string.IsNullOrWhiteSpace(modelName) ? "llama3.2:1b" : modelName;
        _fallback = fallback ?? IntentClassifierDefaults.CreateLocal();

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
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

        // Always calculate fast deterministic fallback signal first
        IntentClassificationResult fallbackResult = await _fallback.ClassifyAsync(text, cancellationToken);
        if (fallbackResult.ToxicityScore >= 0.85)
        {
            // Severe threats / harm wishes are blocked immediately without waiting for LLM
            return fallbackResult;
        }

        try
        {
            // Query local Ollama/LM Studio OpenAI-compatible chat endpoint
            var requestPayload = new
            {
                model = _modelName,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "You are a content moderation classifier. Analyze the text for harassment, malicious wishes, veiled threats, or toxicity. Respond in JSON only with format: {\"is_hostile\": boolean, \"toxicity_score\": number (0.0 to 1.0), \"category\": string}"
                    },
                    new
                    {
                        role = "user",
                        content = text
                    }
                },
                temperature = 0.0,
                format = "json",
                stream = false
            };

            string requestUrl = $"{_endpointUrl}/api/chat";
            using var response = await _httpClient.PostAsJsonAsync(
                requestUrl,
                requestPayload,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return fallbackResult;
            }

            var responseJson = await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);

            if (responseJson.TryGetProperty("message", out var messageElement) &&
                messageElement.TryGetProperty("content", out var contentElement))
            {
                string? rawJson = contentElement.GetString();
                if (!string.IsNullOrWhiteSpace(rawJson))
                {
                    using var parsedDoc = JsonDocument.Parse(rawJson);
                    double score = 0.0;
                    string category = "Local LLM Moderation";

                    if (parsedDoc.RootElement.TryGetProperty("toxicity_score", out var scoreEl))
                    {
                        score = scoreEl.GetDouble();
                    }
                    if (parsedDoc.RootElement.TryGetProperty("category", out var catEl))
                    {
                        category = catEl.GetString() ?? category;
                    }

                    double fusedToxicity = Math.Max(score, fallbackResult.ToxicityScore);
                    return new IntentClassificationResult
                    {
                        IsToxic = fusedToxicity >= 0.60,
                        ToxicityScore = fusedToxicity,
                        ThreatScore = Math.Max(score * 0.9, fallbackResult.ThreatScore),
                        HarassmentScore = Math.Max(score * 0.9, fallbackResult.HarassmentScore),
                        FlaggedCategory = score > fallbackResult.ToxicityScore ? category : fallbackResult.FlaggedCategory,
                        ModelUsed = $"Local LLM ({_modelName}) + Local Hybrid"
                    };
                }
            }

            return fallbackResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // If local LLM is not running or timed out, gracefully return fast on-device fallback
            return fallbackResult;
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient) _httpClient.Dispose();
        _fallback.Dispose();
    }
}
