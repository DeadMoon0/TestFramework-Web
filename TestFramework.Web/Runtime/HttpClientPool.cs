using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace TestFramework.Web.Runtime;

/// <summary>
/// A bounded, least-recently-used cache of <see cref="HttpClient"/> instances keyed by connection settings.
/// </summary>
/// <remarks>
/// <para>
/// Pooling is what lets repeated steps reuse a connection instead of opening a socket per call. The
/// bound is what stops that from becoming a leak: the key includes the base URL, and a container
/// lane hands out a new ephemeral port for every run, so an unbounded cache in a long-lived host
/// (a test explorer left open all day, a runner process reused across runs) grows by one client,
/// one handler, one socket pool and one DNS cache per run and never gives any of it back.
/// </para>
/// <para>
/// Eviction disposes the client it drops. The capacity is far above what any single run uses, so an
/// evicted client belongs to a run that finished long ago; a run holding more than the capacity of
/// distinct endpoints at once would be the pathological case.
/// </para>
/// </remarks>
internal sealed class HttpClientPool(int capacity)
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _tick;

    /// <summary>
    /// Returns the client cached for a key, creating it when it is not there yet.
    /// </summary>
    /// <param name="key">The pool key, covering every setting that changes the connection.</param>
    /// <param name="factory">Creates the client when the key is new.</param>
    public HttpClient GetOrAdd(string key, Func<HttpClient> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        List<HttpClient> evicted = [];
        HttpClient client;

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? existing))
            {
                existing.LastUsed = ++_tick;
                return existing.Client;
            }

            // Created under the lock: constructing a handler is cheap, and two threads racing for
            // the same endpoint would otherwise build a client that is immediately thrown away.
            client = factory();
            _entries[key] = new Entry(client, ++_tick);

            while (_entries.Count > capacity)
            {
                KeyValuePair<string, Entry> oldest = _entries.MinBy(entry => entry.Value.LastUsed);
                _entries.Remove(oldest.Key);
                evicted.Add(oldest.Value.Client);
            }
        }

        foreach (HttpClient stale in evicted)
            stale.Dispose();

        return client;
    }

    /// <summary>
    /// The number of clients currently held.
    /// </summary>
    internal int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    private sealed class Entry(HttpClient client, long lastUsed)
    {
        public HttpClient Client { get; } = client;

        public long LastUsed { get; set; } = lastUsed;
    }
}
