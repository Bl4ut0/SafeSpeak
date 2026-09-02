namespace SafeSpeak.Core.Models;

public enum LivestreamEventType
{
    Chat,
    Gift,
    Follow,
    Share,
    Subscribe,
    Join,
    Like
}

/// <summary>A normalized livestream event, independent of any provider's payload shape.</summary>
public sealed record LivestreamEvent
{
    public string Platform { get; init; } = "Unknown";
    public LivestreamEventType Type { get; init; }
    public string Author { get; init; } = string.Empty;
    public string AuthorDisplayName { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string GiftName { get; init; } = string.Empty;
    public int GiftCount { get; init; } = 1;
    public int DiamondCount { get; init; }
    public bool IsSubscriber { get; init; }
    public bool IsModerator { get; init; }
    public AuthorTier AuthorTier { get; init; } = AuthorTier.Viewer;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public ChatMessage ToChatMessage() => new()
    {
        Author = Author,
        AuthorDisplayName = AuthorDisplayName,
        RawText = Text,
        Platform = Platform,
        IsSubscriber = IsSubscriber,
        IsModerator = IsModerator,
        IsDonor = Type == LivestreamEventType.Gift,
        AuthorTier = AuthorTier,
        TimestampUtc = TimestampUtc
    };
}
