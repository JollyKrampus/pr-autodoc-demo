using System;
using System.Collections.Generic;
using System.Threading;

namespace Utils
{
    /// <summary>
    /// A thread-safe, generic LRU (Least Recently Used) cache with optional per-item expiration,
    /// hit/miss/eviction statistics, and resize capability.
    /// 
    /// - O(1) Set/Get/Remove (amortized)
    /// - Optional TTL per item (TimeSpan?)
    /// - Purges expired entries eagerly on access, and lazily via PurgeExpired()
    /// - OnEvicted event for observability
    /// - Snapshot() for safe enumeration without locking caller
    /// 
    /// Single class intentionally >100 lines by design.
    /// </summary>
    public class LruCache<TKey, TValue>
    {
        private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
        private readonly LinkedList<Entry> _lru; // Most recent at head, least at tail
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

        private int _capacity;
        private TimeSpan? _defaultTtl;

        private long _hits;
        private long _misses;
        private long _evictions;
        private long _expiredRemovals;

        /// <summary>Raised whenever an entry is evicted (LRU or explicit remove).</summary>
        public event Action<TKey, TValue, EvictionReason>? OnEvicted;

        /// <summary>Current capacity. Use Resize() to change safely.</summary>
        public int Capacity
        {
            get { _lock.EnterReadLock(); try { return _capacity; } finally { _lock.ExitReadLock(); } }
        }

        /// <summary>Default TTL applied when Set/GetOrAdd is called without a TTL.</summary>
        public TimeSpan? DefaultTtl
        {
            get { _lock.EnterReadLock(); try { return _defaultTtl; } finally { _lock.ExitReadLock(); } }
        }

        /// <summary>Number of live items (not counting expired ones that haven’t been purged yet).</summary>
        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try { return _map.Count; }
                finally { _lock.ExitReadLock(); }
            }
        }

        /// <summary>Simple statistics snapshot. Resets with ResetStats().</summary>
        public Stats GetStats()
        {
            _lock.EnterReadLock();
            try
            {
                return new Stats
                {
                    Hits = _hits,
                    Misses = _misses,
                    Evictions = _evictions,
                    ExpiredRemovals = _expiredRemovals
                };
            }
            finally { _lock.ExitReadLock(); }
        }

        public void ResetStats()
        {
            _lock.EnterWriteLock();
            try
            {
                _hits = _misses = _evictions = _expiredRemovals = 0;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public LruCache(int capacity, TimeSpan? defaultTtl = null, IEqualityComparer<TKey>? comparer = null)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _defaultTtl = defaultTtl;
            _map = new Dictionary<TKey, LinkedListNode<Entry>>(capacity, comparer);
            _lru = new LinkedList<Entry>();
        }

        /// <summary>
        /// Set/Upsert a value. Touches item to MRU. Optionally override TTL for this write.
        /// </summary>
        public void Set(TKey key, TValue value, TimeSpan? ttl = null)
        {
            var expiry = ttl.HasValue ? DateTimeOffset.UtcNow + ttl.Value
                       : _defaultTtl.HasValue ? DateTimeOffset.UtcNow + _defaultTtl.Value
                       : (DateTimeOffset?)null;

            _lock.EnterWriteLock();
            try
            {
                if (_map.TryGetValue(key, out var node))
                {
                    // Update existing
                    node.Value.Value = value;
                    node.Value.Expiry = expiry;
                    MoveToHead(node);
                }
                else
                {
                    EnsureCapacityLocked(1);
                    var entry = new Entry(key, value, expiry);
                    var newNode = _lru.AddFirst(entry);
                    _map[key] = newNode;
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Try to get a value; touches item to MRU if found and not expired.
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            _lock.EnterUpgradeableReadLock();
            try
            {
                if (_map.TryGetValue(key, out var node))
                {
                    if (IsExpired(node.Value))
                    {
                        _lock.EnterWriteLock();
                        try
                        {
                            RemoveNodeLocked(node, EvictionReason.Expired);
                            _expiredRemovals++;
                        }
                        finally { _lock.ExitWriteLock(); }

                        _misses++;
                        value = default!;
                        return false;
                    }

                    // Touch to MRU
                    _lock.EnterWriteLock();
                    try { MoveToHead(node); }
                    finally { _lock.ExitWriteLock(); }

                    _hits++;
                    value = node.Value.Value!;
                    return true;
                }

                _misses++;
                value = default!;
                return false;
            }
            finally { _lock.ExitUpgradeableReadLock(); }
        }

        /// <summary>
        /// Get or add using a value factory. Factory is only invoked if needed (and under write lock).
        /// </summary>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory, TimeSpan? ttl = null)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));

            if (TryGetValue(key, out var existing)) return existing;

            _lock.EnterWriteLock();
            try
            {
                // Double-check under write lock
                if (_map.TryGetValue(key, out var node2))
                {
                    if (!IsExpired(node2.Value))
                    {
                        MoveToHead(node2);
                        _hits++;
                        return node2.Value.Value!;
                    }

                    RemoveNodeLocked(node2, EvictionReason.Expired);
                    _expiredRemovals++;
                }

                var newValue = factory(key);
                var expiry = ttl.HasValue ? DateTimeOffset.UtcNow + ttl.Value
                           : _defaultTtl.HasValue ? DateTimeOffset.UtcNow + _defaultTtl.Value
                           : (DateTimeOffset?)null;

                EnsureCapacityLocked(1);
                var entry = new Entry(key, newValue, expiry);
                var newNode = _lru.AddFirst(entry);
                _map[key] = newNode;
                return newValue;
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>Returns true if the key is present and not expired. Does not touch LRU order.</summary>
        public bool ContainsKey(TKey key)
        {
            _lock.EnterReadLock();
            try
            {
                if (_map.TryGetValue(key, out var node))
                {
                    return !IsExpired(node.Value);
                }
                return false;
            }
            finally { _lock.ExitReadLock(); }
        }

        /// <summary>Remove if present. Returns true if removed.</summary>
        public bool Remove(TKey key)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_map.TryGetValue(key, out var node))
                {
                    RemoveNodeLocked(node, EvictionReason.Removed);
                    return true;
                }
                return false;
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>Clear the cache. Triggers OnEvicted with reason = Cleared for each entry.</summary>
        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                while (_lru.Last is not null)
                {
                    RemoveNodeLocked(_lru.Last!, EvictionReason.Cleared);
                }
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Resize the cache capacity. If smaller than current count, evicts LRU items.
        /// </summary>
        public void Resize(int newCapacity)
        {
            if (newCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(newCapacity));
            _lock.EnterWriteLock();
            try
            {
                _capacity = newCapacity;
                EnsureCapacityLocked(0);
            }
            finally { _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Purge expired items by scanning up to <paramref name="maxToScan"/> nodes from LRU tail.
        /// Returns number of items removed.
        /// </summary>
        public int PurgeExpired(int maxToScan = 64)
        {
            if (maxToScan <= 0) return 0;

            int removed = 0;
            _lock.EnterWriteLock();
            try
            {
                var node = _lru.Last;
                while (node is not null && removed < maxToScan)
                {
                    var prev = node.Previous;
                    if (IsExpired(node.Value))
                    {
                        RemoveNodeLocked(node, EvictionReason.Expired);
                        _expiredRemovals++;
                        removed++;
                    }
                    node = prev;
                }
            }
            finally { _lock.ExitWriteLock(); }

            return removed;
        }

        /// <summary>
        /// Get a stable snapshot of current (non-expired) entries as key/value pairs.
        /// </summary>
        public IReadOnlyList<KeyValuePair<TKey, TValue>> Snapshot()
        {
            var list = new List<KeyValuePair<TKey, TValue>>();
            _lock.EnterReadLock();
            try
            {
                for (var node = _lru.First; node is not null; node = node.Next)
                {
                    var e = node.Value;
                    if (!IsExpired(e))
                        list.Add(new KeyValuePair<TKey, TValue>(e.Key, e.Value!));
                }
            }
            finally { _lock.ExitReadLock(); }
            return list;
        }

        /// <summary>Sets the default TTL used when none is supplied to Set/GetOrAdd.</summary>
        public void SetDefaultTtl(TimeSpan? defaultTtl)
        {
            _lock.EnterWriteLock();
            try { _defaultTtl = defaultTtl; }
            finally { _lock.ExitWriteLock(); }
        }

        public override string ToString()
        {
            _lock.EnterReadLock();
            try
            {
                var st = GetStats();
                return $"LruCache<{typeof(TKey).Name},{typeof(TValue).Name}> Cap={_capacity} Count={_map.Count} " +
                       $"Hits={st.Hits} Misses={st.Misses} Evictions={st.Evictions} Expired={st.ExpiredRemovals}";
            }
            finally { _lock.ExitReadLock(); }
        }

        // -------------------- Internals --------------------

        private void EnsureCapacityLocked(int incoming)
        {
            // Evict while over capacity
            while (_map.Count + incoming > _capacity && _lru.Last is not null)
            {
                RemoveNodeLocked(_lru.Last, EvictionReason.Capacity);
            }
        }

        private static bool IsExpired(in Entry e)
            => e.Expiry.HasValue && e.Expiry.Value <= DateTimeOffset.UtcNow;

        private void MoveToHead(LinkedListNode<Entry> node)
        {
            if (node.List != _lru) return; // defensive
            if (node != _lru.First)
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
            }
        }

        private void RemoveNodeLocked(LinkedListNode<Entry> node, EvictionReason reason)
        {
            var e = node.Value;
            _lru.Remove(node);
            _map.Remove(e.Key);
            if (reason is EvictionReason.Capacity or EvictionReason.Expired) _evictions++;
            OnEvicted?.Invoke(e.Key, e.Value!, reason);
        }

        // Stored entry (class to allow mutation without relinking the node)
        private sealed class Entry
        {
            public TKey Key { get; }
            public TValue? Value { get; set; }
            public DateTimeOffset? Expiry { get; set; }

            public Entry(TKey key, TValue? value, DateTimeOffset? expiry)
            {
                Key = key;
                Value = value;
                Expiry = expiry;
            }
        }

        public enum EvictionReason
        {
            Capacity,
            Expired,
            Removed,
            Cleared
        }

        public struct Stats
        {
            public long Hits;
            public long Misses;
            public long Evictions;
            public long ExpiredRemovals;

            public override string ToString()
                => $"Hits={Hits}, Misses={Misses}, Evictions={Evictions}, ExpiredRemovals={ExpiredRemovals}";
        }
    }
}
