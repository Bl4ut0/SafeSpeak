namespace SafeSpeak.Core.Models;

/// <summary>
/// Represents the authority or subscription level of the message author.
/// </summary>
public enum AuthorTier
{
    Viewer = 0,
    Follower = 1,
    Subscriber = 2,
    Moderator = 3,
    Host = 4
}

/// <summary>
/// Represents a normalized incoming chat message received from TikFinity or a test simulator.
/// </summary>
public sealed record ChatMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Author { get; init; } = string.Empty;
    public string AuthorDisplayName { get; init; } = string.Empty;
    public string RawText { get; init; } = string.Empty;
    public AuthorTier AuthorTier { get; init; } = AuthorTier.Viewer;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Platform { get; init; } = "TikTok";
    public bool IsSubscriber { get; init; }
    public bool IsModerator { get; init; }
}
