using SafeSpeak.Core.Chat;

namespace SafeSpeak.Core.Queueing;

public sealed record TtsQueueItem(ChatMessage Message, string SpeakableText);

public sealed class TtsQueue(int capacity = 50)
{
    private readonly Queue<TtsQueueItem> _items = new();
    private readonly Lock _lock = new();

    public bool IsPaused { get; private set; }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    public bool TryEnqueue(TtsQueueItem item)
    {
        lock (_lock)
        {
            if (_items.Count >= capacity)
            {
                return false;
            }

            _items.Enqueue(item);
            return true;
        }
    }

    public bool TryDequeue(out TtsQueueItem? item)
    {
        lock (_lock)
        {
            if (IsPaused || _items.Count == 0)
            {
                item = null;
                return false;
            }

            item = _items.Dequeue();
            return true;
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            IsPaused = true;
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            IsPaused = false;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }
}
