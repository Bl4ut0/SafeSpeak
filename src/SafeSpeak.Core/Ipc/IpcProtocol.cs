namespace SafeSpeak.Core.Ipc;

public sealed record IpcCommandMessage
{
    public string Command { get; init; } = string.Empty;
    public string Parameter { get; init; } = string.Empty;
}

public sealed record IpcStateBroadcast
{
    public bool IsArmed { get; init; }
    public bool IsAutoPlay { get; init; }
    public bool IsPaused { get; init; }
    public bool IsSpeaking { get; init; }
    public int QueueCount { get; init; }
    public string ConnectionState { get; init; } = "Disconnected";
    public bool IsConnected { get; init; }
}
