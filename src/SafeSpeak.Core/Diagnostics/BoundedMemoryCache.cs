namespace SafeSpeak.Core.Diagnostics;

/// <summary>Session-local LRU cache with fixed expiry and a total cost limit.</summary>
internal sealed class BoundedMemoryCache<TKey, TValue> where TKey : notnull
{
    private sealed record Entry(TValue Value, DateTimeOffset Expires, long Cost, LinkedListNode<TKey> Node);
    private readonly object _gate = new();
    private readonly Dictionary<TKey, Entry> _entries = new();
    private readonly LinkedList<TKey> _lru = new();
    private readonly long _limit;
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _time;
    private long _cost;
    private readonly int _maximumEntries;

    public BoundedMemoryCache(long limit, TimeSpan ttl, TimeProvider? time = null, int maximumEntries = int.MaxValue)
    {
        if (limit < 1 || ttl <= TimeSpan.Zero || maximumEntries < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        _maximumEntries = maximumEntries;
        _limit = limit; _ttl = ttl; _time = time ?? TimeProvider.System;
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? entry))
            {
                if (entry.Expires > _time.GetUtcNow())
                {
                    _lru.Remove(entry.Node); _lru.AddLast(entry.Node);
                    value = entry.Value; return true;
                }
                Remove(key);
            }
            value = default!; return false;
        }
    }

    public void Set(TKey key, TValue value, long cost = 1)
    {
        if (cost < 1 || cost > _limit) return;
        lock (_gate)
        {
            Remove(key);
            while ((_cost + cost > _limit || _entries.Count >= _maximumEntries) && _lru.First is not null) Remove(_lru.First.Value);
            var node = _lru.AddLast(key);
            _entries[key] = new(value, _time.GetUtcNow() + _ttl, cost, node);
            _cost += cost;
        }
    }

    public void Clear()
    {
        lock (_gate) { _entries.Clear(); _lru.Clear(); _cost = 0; }
    }

    private void Remove(TKey key)
    {
        if (!_entries.Remove(key, out Entry? entry)) return;
        _cost -= entry.Cost; _lru.Remove(entry.Node);
    }
}
