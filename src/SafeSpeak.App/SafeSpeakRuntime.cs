using SafeSpeak.Core.Control;
using SafeSpeak.Core.Chat;
using SafeSpeak.Core.Moderation;
using SafeSpeak.Core.Queueing;

namespace SafeSpeak.App;

public sealed class SafeSpeakRuntime : IControlCommandHandler
{
    private readonly TtsQueue _queue = new();
    private readonly Blocklist _blocklist = new(["badword"]);
    private readonly UserRateLimiter _rateLimiter = new();
    private readonly IToxicityClassifier _classifier = new UnavailableToxicityClassifier();
    private ConnectionState _connectionState = ConnectionState.Disconnected;
    private bool _armed;
    private bool _automaticPlayback;
    private bool _englishOnly;

    public SafeSpeakRuntime(bool englishOnly = true)
    {
        _englishOnly = englishOnly;
    }

    public event EventHandler<SafeSpeakControlState>? StateChanged;

    public event EventHandler<string>? ActivityChanged;

    public SafeSpeakControlState State => new(
        _connectionState == ConnectionState.Connected,
        _armed,
        _automaticPlayback,
        _queue.IsPaused,
        _englishOnly,
        _queue.Count);

    public ConnectionState ConnectionState => _connectionState;

    public int QueueCapacity => _queue.Capacity;

    public IReadOnlyList<TtsQueueItem> GetQueueSnapshot() => _queue.Snapshot();

    public void SetConnectionState(ConnectionState state)
    {
        _connectionState = state;
        if (state != ConnectionState.Connected)
        {
            _armed = false;
            _automaticPlayback = false;
        }

        PublishState();
    }

    public async ValueTask ProcessChatMessageAsync(
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        if (_connectionState != ConnectionState.Connected || !_armed)
        {
            return;
        }

        if (!_rateLimiter.TryAcquire(message.UserId, message.ReceivedAt))
        {
            ActivityChanged?.Invoke(this, "A chat message was rejected by the user cooldown.");
            return;
        }

        var pipeline = new ModerationPipeline(
            _blocklist,
            _classifier,
            new ModerationOptions
            {
                EnglishOnly = _englishOnly,
                EnableClassifier = false,
            });
        ModerationDecision decision = await pipeline.EvaluateAsync(message, cancellationToken);

        if (decision.Disposition != ModerationDisposition.Approved)
        {
            ActivityChanged?.Invoke(this, $"A chat message was not queued. Reason: {decision.Reason}.");
            return;
        }

        bool queued = _queue.TryEnqueue(new TtsQueueItem(message, decision.SpeakableText));
        ActivityChanged?.Invoke(this, queued
            ? "An approved chat message was added to the queue."
            : "The approved-message queue is full; the new message was not queued.");
        PublishState();
    }

    public ValueTask<ControlResponse> HandleAsync(
        ControlRequest request,
        CancellationToken cancellationToken = default)
    {
        CommandOutcome outcome = request.Command switch
        {
            ControlCommands.ToggleArmed => ToggleArmed(),
            ControlCommands.ToggleAutomaticPlayback => ToggleAutomaticPlayback(),
            ControlCommands.PlayNext => PlayNext(),
            ControlCommands.SkipCurrent => CommandOutcome.Failure("Speech output is not implemented in this build"),
            ControlCommands.TogglePause => TogglePause(),
            ControlCommands.ClearQueue => ClearQueue(),
            ControlCommands.EmergencyStop => EmergencyStop(),
            ControlCommands.AnnounceStatus => BuildStatus(),
            ControlCommands.ToggleEnglishOnly => ToggleEnglishOnly(),
            ControlCommands.CycleAudience => CommandOutcome.Failure("Audience mode selection is not implemented in this build"),
            ControlCommands.CycleStrictness => CommandOutcome.Failure("Moderation strictness selection is not implemented in this build"),
            ControlCommands.PlayPreset => CommandOutcome.Failure("Preset speech is not implemented in this build"),
            _ => CommandOutcome.Failure("Unknown SafeSpeak command"),
        };

        PublishState();
        return ValueTask.FromResult(new ControlResponse(
            "response",
            outcome.Success,
            request.Command,
            outcome.Message,
            State));
    }

    private CommandOutcome ToggleArmed()
    {
        if (_connectionState != ConnectionState.Connected)
        {
            _armed = false;
            return CommandOutcome.Failure("Cannot arm: TikFinity is disconnected");
        }

        _armed = !_armed;
        return CommandOutcome.Successful(_armed ? "TTS armed" : "TTS disarmed");
    }

    private CommandOutcome ToggleAutomaticPlayback()
    {
        _automaticPlayback = false;
        return CommandOutcome.Failure("Automatic playback is unavailable until speech output is implemented");
    }

    private CommandOutcome TogglePause()
    {
        if (_queue.IsPaused)
        {
            _queue.Resume();
            return CommandOutcome.Successful("Queue resumed");
        }

        _queue.Pause();
        return CommandOutcome.Successful("Queue paused");
    }

    private static CommandOutcome PlayNext() =>
        CommandOutcome.Failure("Speech output is not implemented in this build");

    private CommandOutcome ClearQueue()
    {
        _queue.Clear();
        return CommandOutcome.Successful("Queue cleared");
    }

    private CommandOutcome EmergencyStop()
    {
        _armed = false;
        _automaticPlayback = false;
        _queue.Clear();
        return CommandOutcome.Successful("Emergency stop activated. TTS disarmed and queue cleared");
    }

    private CommandOutcome ToggleEnglishOnly()
    {
        _englishOnly = !_englishOnly;
        return CommandOutcome.Successful(_englishOnly ? "English-only mode enabled" : "English-only mode disabled");
    }

    private CommandOutcome BuildStatus() => CommandOutcome.Successful(
        $"TikFinity bridge {_connectionState.ToString().ToLowerInvariant()}. " +
        $"TTS {(_armed ? "armed" : "disarmed")}. " +
        $"Queue contains {_queue.Count} messages{(_queue.IsPaused ? " and is paused" : string.Empty)}. " +
        $"English-only mode {(_englishOnly ? "enabled" : "disabled")}.");

    private void PublishState() => StateChanged?.Invoke(this, State);

    private readonly record struct CommandOutcome(bool Success, string Message)
    {
        public static CommandOutcome Successful(string message) => new(true, message);

        public static CommandOutcome Failure(string message) => new(false, message);
    }
}
