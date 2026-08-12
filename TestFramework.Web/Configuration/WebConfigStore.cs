using System.Collections.Generic;

namespace TestFramework.Web.Configuration;

/// <summary>
/// Thread-safe runtime store for web configuration records keyed by logical identifier.
/// </summary>
/// <typeparam name="TConfig">The configuration record type stored in the collection.</typeparam>
/// <remarks>
/// Environments mutate this store at run time to publish resolved endpoints. It is deliberately
/// named separately from the Azure module store so both can be imported in one test file.
/// </remarks>
public class WebConfigStore<TConfig>
{
    private readonly Dictionary<string, TConfig> _config = [];
    private readonly object _syncRoot = new();

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
    /// <param name="identifier">The logical identifier used in the web DSL.</param>
    /// <param name="config">The configuration instance to store.</param>
    public void AddConfig(string identifier, TConfig config)
    {
        lock (_syncRoot)
        {
            _config[identifier] = config;
        }
    }

    /// <summary>
    /// Retrieves the configuration associated with an identifier.
    /// </summary>
    /// <param name="identifier">The logical identifier to resolve.</param>
    /// <returns>The stored configuration instance.</returns>
    public TConfig GetConfig(string identifier)
    {
        lock (_syncRoot)
        {
            return _config[identifier];
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
