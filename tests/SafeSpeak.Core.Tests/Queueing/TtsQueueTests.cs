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

    [Fact]
    public void SnapshotPreservesQueueOrderWithoutRemovingItems()
    {
        var queue = new TtsQueue(capacity: 3);
        var first = new TtsQueueItem(
            new ChatMessage("1", "first", "First", "hello", AudienceRole.Guest, DateTimeOffset.UtcNow),
            "hello");
        var second = new TtsQueueItem(
            new ChatMessage("2", "second", "Second", "welcome", AudienceRole.Follower, DateTimeOffset.UtcNow),
            "welcome");

        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(second));

        IReadOnlyList<TtsQueueItem> snapshot = queue.Snapshot();

        Assert.Equal(3, queue.Capacity);
        Assert.Equal([first, second], snapshot);
        Assert.Equal(2, queue.Count);
    }
}
