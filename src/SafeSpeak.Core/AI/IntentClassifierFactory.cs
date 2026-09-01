using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.AI;

/// <summary>
/// Factory for instantiating the user-selected intent classification engine.
/// Default: Fast local ONNX MiniLM + Heuristics (CPU offline).
/// Optional: Google Perspective API (Deep Cloud Intent Analysis with 0 local CPU load).
/// </summary>
public static class IntentClassifierFactory
{
    public const string LocalHybridId = "local_hybrid";
    public const string LocalLlmId = "local_llm";
    public const string GooglePerspectiveId = "google_perspective";

    public static IIntentClassifier Create(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string engineId = settings.SelectedIntentEngineId?.ToLowerInvariant() ?? LocalHybridId;

        if (engineId == LocalLlmId)
        {
            return new LocalEndpointIntentClassifier(
                endpointUrl: settings.LocalLlmEndpointUrl,
                modelName: settings.LocalLlmModelName,
                fallback: new LocalOnnxIntentClassifier());
        }

        if (engineId == GooglePerspectiveId && !string.IsNullOrWhiteSpace(settings.PerspectiveApiKey))
        {
            return new GooglePerspectiveClassifier(
                settings.PerspectiveApiKey,
                localFallback: new LocalOnnxIntentClassifier());
        }

        // Default fast on-device CPU hybrid
        return new LocalOnnxIntentClassifier();
    }
}
