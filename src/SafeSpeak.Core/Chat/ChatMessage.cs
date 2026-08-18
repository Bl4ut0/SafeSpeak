namespace SafeSpeak.Core.Chat;

public sealed record ChatMessage(
    string MessageId,
    string UserId,
    string DisplayName,
    string Text,
    AudienceRole AudienceRole,
    DateTimeOffset ReceivedAt);
