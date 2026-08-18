namespace SafeSpeak.Core.Control;

public static class ControlCommands
{
    public const string ToggleArmed = "tts.toggleArmed";
    public const string ToggleAutomaticPlayback = "tts.toggleAutomaticPlayback";
    public const string PlayNext = "queue.playNext";
    public const string SkipCurrent = "queue.skipCurrent";
    public const string TogglePause = "queue.togglePause";
    public const string ClearQueue = "queue.clear";
    public const string EmergencyStop = "tts.emergencyStop";
    public const string AnnounceStatus = "status.announce";
    public const string CycleAudience = "moderation.cycleAudience";
    public const string CycleStrictness = "moderation.cycleStrictness";
    public const string ToggleEnglishOnly = "moderation.toggleEnglishOnly";
    public const string PlayPreset = "preset.play";
}

public sealed record ControlRequest(string Type, string Command, string? Argument = null);

public sealed record ControlResponse(
    string Type,
    bool Success,
    string Command,
    string? Message = null,
    SafeSpeakControlState? State = null);

public sealed record SafeSpeakControlState(
    bool Connected,
    bool Armed,
    bool AutomaticPlayback,
    bool QueuePaused,
    bool EnglishOnly,
    int QueueCount);

public interface IControlCommandHandler
{
    ValueTask<ControlResponse> HandleAsync(ControlRequest request, CancellationToken cancellationToken = default);
}
