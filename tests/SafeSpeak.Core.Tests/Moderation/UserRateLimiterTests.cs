using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests.Moderation;

public sealed class UserRateLimiterTests
{
    [Fact]
    public void RejectsMessagesBeyondLimitInsideWindow()
    {
        var limiter = new UserRateLimiter(2, TimeSpan.FromSeconds(10));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.True(limiter.TryAcquire("viewer", now));
        Assert.True(limiter.TryAcquire("viewer", now.AddSeconds(1)));
        Assert.False(limiter.TryAcquire("viewer", now.AddSeconds(2)));
        Assert.True(limiter.TryAcquire("viewer", now.AddSeconds(11)));
    }
}
