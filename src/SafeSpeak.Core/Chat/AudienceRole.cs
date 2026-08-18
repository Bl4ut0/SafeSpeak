namespace SafeSpeak.Core.Chat;

[Flags]
public enum AudienceRole
{
    None = 0,
    Guest = 1,
    Follower = 2,
    Subscriber = 4,
    Moderator = 8,
    Trusted = 16,
}
