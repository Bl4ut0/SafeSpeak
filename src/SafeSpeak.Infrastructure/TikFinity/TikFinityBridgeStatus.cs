using SafeSpeak.Core.Chat;

namespace SafeSpeak.Infrastructure.TikFinity;

public sealed record TikFinityBridgeStatus(
    ConnectionState State,
    Uri Endpoint,
    long ConnectionAttempts,
    long TextEventsReceived,
    long ChatMessagesAccepted,
    long EventsIgnored,
    DateTimeOffset? LastChatMessageAt,
    string? LastError);
