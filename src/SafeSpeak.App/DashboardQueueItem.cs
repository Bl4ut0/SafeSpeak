using SafeSpeak.Core.Chat;

namespace SafeSpeak.App;

internal sealed record DashboardQueueItem(
    int Position,
    string SpeakableText,
    DateTimeOffset ReceivedAt,
    AudienceRole AudienceRole)
{
    public string PositionLabel => $"Queue position {Position}";

    public string Metadata => $"Received {ReceivedAt.ToLocalTime():t}; audience role: {AudienceRole}";

    public string AccessibleName => $"Queue position {Position}. {SpeakableText}. {Metadata}.";
}
