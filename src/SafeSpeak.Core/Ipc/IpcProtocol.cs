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
    public string AudienceMode { get; init; } = "All";
    public string Strictness { get; init; } = "High";
    public bool EnglishOnly { get; init; } = true;
    public bool RejectMixedScripts { get; init; } = true;
    public bool SpeakUsernames { get; init; } = false;
    public bool AiClassificationEnabled { get; init; } = false;
    public bool IsConnected { get; init; }
    public bool AnnounceChatMessages { get; init; }
    public bool AnnounceGifts { get; init; }
    public bool AnnounceFollows { get; init; }
    public bool AnnounceShares { get; init; }
    public bool AnnounceSubscriptions { get; init; }
    public bool AnnounceJoins { get; init; }
    public bool AnnounceLikes { get; init; }
    public bool BroadcastOutputEnabled { get; init; }
    public bool PrivateMonitorEnabled { get; init; }
    public bool UseHighContrastTheme { get; init; }
}
