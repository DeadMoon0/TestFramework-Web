using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web.Configuration;

/// <summary>
/// What was declared about each web resource, keyed by logical identifier.
/// </summary>
/// <typeparam name="TConfig">The configuration record type stored in the collection.</typeparam>
/// <remarks>
/// <para>
/// <strong>Declarations only, and no longer writable from outside this package.</strong> Environments used to
/// mutate this store at run time to publish where a container had ended up, and four of them did: a store that
/// can still be written to at run time cannot be trusted to describe what a person intended, which is §4's
/// corollary and was simply not true here. Where a resource actually is now travels as a resource value on the
/// run, so the only writer left is this package's own loader and <see cref="AddConfig"/> is internal.
/// </para>
/// <para>
/// A fixture with no configuration file still declares entries directly - through the constructor or
/// <see cref="Create"/>, at construction, where a declaration belongs. What it can no longer do is change one
/// after a run has started reading them.
/// </para>
/// <para>
/// Named separately from the Azure module's store so both can be imported in one test file.
/// </para>
/// </remarks>
public class WebConfigStore<TConfig>
{
    private readonly Dictionary<string, TConfig> _config = [];
    private readonly object _syncRoot = new();

    /// <summary>
    /// Creates an empty store, for a loader to fill.
    /// </summary>
    public WebConfigStore()
    {
    }

    /// <summary>
    /// Creates a store holding what a caller declares, all of it at once.
    /// </summary>
    /// <remarks>
    /// The road for a fixture that has no configuration file: everything it declares arrives here, and the
    /// store is read-only from then on.
    /// </remarks>
    /// <param name="entries">The identifier/config pairs to declare.</param>
    public WebConfigStore(IEnumerable<KeyValuePair<string, TConfig>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (KeyValuePair<string, TConfig> entry in entries)
        {
            this.AddConfig(entry.Key, entry.Value);
        }
    }

    /// <summary>
    /// Creates a store pre-populated with a single identifier/config pair.
    /// </summary>
    /// <param name="identifier">The logical identifier to register.</param>
    /// <param name="config">The configuration instance to store.</param>
    /// <returns>A new store containing the provided entry.</returns>
    public static WebConfigStore<TConfig> Create(string identifier, TConfig config)
    {
        WebConfigStore<TConfig> store = new();
        store.AddConfig(identifier, config);
        return store;
    }

    /// <summary>
    /// Adds or replaces the configuration for an identifier.
    /// </summary>
    /// <remarks>
    /// Internal: a declaration is made when it is loaded, and a run that could redirect one afterwards is a
    /// run that cannot say what it tested against.
    /// </remarks>
    /// <param name="identifier">The logical identifier used in the web DSL.</param>
    /// <param name="config">The configuration instance to store.</param>
    internal void AddConfig(string identifier, TConfig config)
    {
        lock (_syncRoot)
        {
            _config[identifier] = config;
        }
    }

    /// <summary>
    /// Retrieves the configuration associated with an identifier.
    /// </summary>
    /// <remarks>
    /// A missing entry is almost always a typo, so the failure lists what is declared. This used to index the
    /// dictionary directly and throw a bare <see cref="KeyNotFoundException"/> - no message, nothing to act
    /// on, and §7 asks a failure to say what is true, what is available and what to type next.
    /// </remarks>
    /// <param name="identifier">The logical identifier to resolve.</param>
    /// <returns>The stored configuration instance.</returns>
    /// <exception cref="FrameworkConfigurationException">Nothing is declared under that identifier.</exception>
    public TConfig GetConfig(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        lock (_syncRoot)
        {
            if (_config.TryGetValue(identifier, out TConfig? config))
            {
                return config;
            }

            throw new FrameworkConfigurationException(
                $"Nothing is configured as {typeof(TConfig).Name} under '{identifier}'.",
                [
                    "Add the entry to your configuration, or let the environment provide the resource and ask the run for it instead.",
                ],
                [.. _config.Keys.OrderBy(static key => key, StringComparer.Ordinal)]);
        }
    }

    /// <summary>
    /// Attempts to retrieve the configuration associated with an identifier.
    /// </summary>
    /// <param name="identifier">The logical identifier to resolve.</param>
    /// <param name="config">The stored configuration instance when present.</param>
    /// <returns><see langword="true"/> when the identifier is registered.</returns>
    public bool TryGetConfig(string identifier, out TConfig? config)
    {
        lock (_syncRoot)
        {
            return _config.TryGetValue(identifier, out config);
        }
    }

    /// <summary>
    /// Returns a copy of the current identifier/config map.
    /// </summary>
    /// <returns>A snapshot of the stored configuration entries.</returns>
    public IReadOnlyDictionary<string, TConfig> Snapshot()
    {
        lock (_syncRoot)
        {
            return new Dictionary<string, TConfig>(_config);
        }
    }
}
