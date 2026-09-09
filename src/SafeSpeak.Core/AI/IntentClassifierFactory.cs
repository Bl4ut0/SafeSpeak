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

    public static IIntentClassifier Create(
        AppSettings settings,
        string? managedQwenEndpointUrl = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.ModerationModel ==
            ModerationModelPreference.Qwen3Guard06BCompressed)
        {
            return new Qwen3GuardIntentClassifier(
                endpointUrl: string.IsNullOrWhiteSpace(managedQwenEndpointUrl)
                    ? Qwen3GuardIntentClassifier.DefaultEndpointUrl
                    : managedQwenEndpointUrl,
                fallback: IntentClassifierDefaults.CreateLocal());
        }

        string engineId = settings.SelectedIntentEngineId?.ToLowerInvariant() ?? LocalHybridId;

        if (engineId == LocalLlmId)
        {
            return new LocalEndpointIntentClassifier(
                endpointUrl: settings.LocalLlmEndpointUrl,
                modelName: settings.LocalLlmModelName,
                fallback: IntentClassifierDefaults.CreateLocal());
        }

        if (engineId == GooglePerspectiveId && !string.IsNullOrWhiteSpace(settings.PerspectiveApiKey))
        {
            return new GooglePerspectiveClassifier(
                settings.PerspectiveApiKey,
                localFallback: IntentClassifierDefaults.CreateLocal());
        }

        // Default fast on-device CPU hybrid
        return IntentClassifierDefaults.CreateLocal();
    }
}
