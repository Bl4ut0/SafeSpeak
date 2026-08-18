using SafeSpeak.Core.Chat;
using SafeSpeak.Core.Queueing;

namespace SafeSpeak.Core.Tests.Queueing;

public sealed class TtsQueueTests
{
    [Fact]
    public void EnforcesCapacityAndPauseState()
    {
        var queue = new TtsQueue(capacity: 1);
        var item = new TtsQueueItem(
            new ChatMessage("1", "viewer", "Viewer", "hello", AudienceRole.Guest, DateTimeOffset.UtcNow),
            "hello");

        Assert.True(queue.TryEnqueue(item));
        Assert.False(queue.TryEnqueue(item));

        queue.Pause();
        Assert.False(queue.TryDequeue(out _));

        queue.Resume();
        Assert.True(queue.TryDequeue(out TtsQueueItem? dequeued));
        Assert.Equal(item, dequeued);
    }
}
