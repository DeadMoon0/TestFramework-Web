using System;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Exceptions;

namespace TestFramework.Web.Configuration;

/// <summary>
/// Resolves API configuration for a run, with an actionable error when it is absent.
/// </summary>
public static class ApiConfigResolver
{
    /// <summary>
    /// Resolves the configuration registered for an API identifier.
    /// </summary>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    /// <param name="identifier">The API identifier to resolve.</param>
    /// <returns>The resolved configuration.</returns>
    /// <exception cref="ApiConfigurationValidationException">No configuration is registered for the identifier.</exception>
    public static ApiConfig Resolve(IServiceProvider serviceProvider, string identifier)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        WebConfigStore<ApiConfig>? store = serviceProvider.GetService<WebConfigStore<ApiConfig>>();
        if (store is null)
            throw ApiConfigurationValidationException.MissingIdentifier(identifier, []);

        if (store.TryGetConfig(identifier, out ApiConfig? config) && config is not null)
            return config;

        throw ApiConfigurationValidationException.MissingIdentifier(identifier, store.Snapshot().Keys);
    }
}
