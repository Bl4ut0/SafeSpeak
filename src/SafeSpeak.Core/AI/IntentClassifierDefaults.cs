namespace SafeSpeak.Core.AI;

/// <summary>
/// Selects the strongest built-in offline classifier available on the current
/// platform without making portable consumers depend on the Windows ONNX bundle.
/// </summary>
public static class IntentClassifierDefaults
{
    public static IIntentClassifier CreateLocal()
    {
#if WINDOWS
        return new LocalOnnxIntentClassifier();
#else
        return new HeuristicIntentClassifier();
#endif
    }
}
