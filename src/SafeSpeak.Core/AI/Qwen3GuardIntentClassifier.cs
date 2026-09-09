using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SafeSpeak.Core.AI;

/// <summary>
/// Uses the optional compressed Qwen3Guard 0.6B model through a loopback
/// Ollama service. The bundled classifier remains active as a fallback, so
/// selecting this option never makes moderation depend on a network service.
/// </summary>
public sealed partial class Qwen3GuardIntentClassifier : IIntentClassifier
{
    private const double ControversialScore = 0.55;
    public const string DefaultEndpointUrl = "http://localhost:11434";
    public const string DefaultModelName = "sileader/qwen3guard:0.6b";

    private readonly HttpClient _httpClient;
    private readonly IIntentClassifier _fallback;
    private readonly bool _ownsHttpClient;
    private readonly string _endpointUrl;
    private readonly string _modelName;
    private int _hasSuccessfulResponse;
    private bool _disposed;

    public string ModelName => "Qwen3Guard 0.6B compressed";
    public bool IsModelLoaded => Volatile.Read(ref _hasSuccessfulResponse) != 0;

    public Qwen3GuardIntentClassifier(
        string endpointUrl = DefaultEndpointUrl,
        string modelName = DefaultModelName,
        IIntentClassifier? fallback = null,
        HttpClient? httpClient = null)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme is not ("http" or "https") ||
            !endpoint.IsLoopback)
        {
            throw new ArgumentException(
                "The Qwen3Guard endpoint must be an HTTP or HTTPS loopback address.",
                nameof(endpointUrl));
        }

        _endpointUrl = endpoint.AbsoluteUri.TrimEnd('/');
        _modelName = string.IsNullOrWhiteSpace(modelName)
            ? DefaultModelName
            : modelName.Trim();
        _fallback = fallback ?? IntentClassifierDefaults.CreateLocal();
        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
        }
    }

    public async Task<IntentClassificationResult> ClassifyAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IntentClassificationResult fallbackResult =
            await _fallback.ClassifyAsync(text, cancellationToken);
        if (string.IsNullOrWhiteSpace(text) || fallbackResult.ToxicityScore >= 0.85)
        {
            return fallbackResult;
        }

        try
        {
            var payload = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "user", content = text }
                },
                stream = false,
                options = new
                {
                    temperature = 0.0,
                    num_predict = 48
                }
            };

            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                $"{_endpointUrl}/api/chat",
                payload,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return fallbackResult;
            }

            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("message", out JsonElement message) ||
                !message.TryGetProperty("content", out JsonElement contentElement))
            {
                return fallbackResult;
            }

            string content = contentElement.GetString() ?? string.Empty;
            Match verdictMatch = SafetyVerdictRegex().Match(content);
            if (!verdictMatch.Success)
            {
                return fallbackResult;
            }

            Volatile.Write(ref _hasSuccessfulResponse, 1);
            string verdict = verdictMatch.Groups[1].Value;
            string category = CategoryRegex().Match(content) is { Success: true } categoryMatch
                ? categoryMatch.Groups[1].Value.Trim()
                : "None";
            return Fuse(verdict, category, fallbackResult);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return fallbackResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return fallbackResult;
        }
    }

    private static IntentClassificationResult Fuse(
        string verdict,
        string category,
        IntentClassificationResult fallback)
    {
        double qwenScore = verdict.Equals("Unsafe", StringComparison.OrdinalIgnoreCase)
            ? 0.95
            : verdict.Equals("Controversial", StringComparison.OrdinalIgnoreCase)
                // A controversial label is ambiguous rather than proof of abuse.
                // Keep it below Strong (level 3) and above Maximum (level 4),
                // while deterministic threats and the bundled classifier remain
                // able to raise the fused score independently.
                ? ControversialScore
                : 0.0;
        double toxicity = Math.Max(qwenScore, fallback.ToxicityScore);
        bool violent = ContainsAny(category, "violent", "violence", "self-harm", "suicide");
        bool harassment = ContainsAny(
            category,
            "hate",
            "harassment",
            "bullying",
            "discrimination");
        bool sexual = ContainsAny(category, "sexual", "obscene");

        return new IntentClassificationResult
        {
            IsToxic = toxicity >= 0.60,
            ToxicityScore = toxicity,
            SevereToxicityScore = Math.Max(
                fallback.SevereToxicityScore,
                qwenScore >= 0.90 ? qwenScore : 0.0),
            ObsceneScore = Math.Max(fallback.ObsceneScore, sexual ? qwenScore : 0.0),
            ThreatScore = Math.Max(fallback.ThreatScore, violent ? qwenScore : 0.0),
            HarassmentScore = Math.Max(
                fallback.HarassmentScore,
                harassment ? qwenScore : 0.0),
            InsultScore = fallback.InsultScore,
            IdentityHateScore = Math.Max(
                fallback.IdentityHateScore,
                harassment ? qwenScore : 0.0),
            FlaggedCategory = qwenScore > fallback.ToxicityScore
                ? $"Qwen3Guard {category}"
                : fallback.FlaggedCategory,
            ModelUsed = "Qwen3Guard 0.6B compressed + built-in local fallback"
        };
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(
        @"(?im)^\s*Safety\s*:\s*(Safe|Unsafe|Controversial)\s*$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 50)]
    private static partial Regex SafetyVerdictRegex();

    [GeneratedRegex(
        @"(?im)^\s*Categories?\s*:\s*([^\r\n]+)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 50)]
    private static partial Regex CategoryRegex();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        _fallback.Dispose();
    }
}
