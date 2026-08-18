namespace SafeSpeak.Core.Moderation;

public sealed class UserRateLimiter(int maximumMessages = 3, TimeSpan? window = null)
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _activity = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private readonly TimeSpan _window = window ?? TimeSpan.FromSeconds(15);

    public bool TryAcquire(string userId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_activity.TryGetValue(userId, out Queue<DateTimeOffset>? timestamps))
            {
                timestamps = new();
                _activity[userId] = timestamps;
            }

            while (timestamps.TryPeek(out DateTimeOffset oldest) && now - oldest >= _window)
            {
                timestamps.Dequeue();
            }

            if (timestamps.Count >= maximumMessages)
            {
                return false;
            }

            timestamps.Enqueue(now);
            return true;
        }
    }
}
