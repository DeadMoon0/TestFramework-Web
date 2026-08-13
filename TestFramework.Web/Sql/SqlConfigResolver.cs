using System;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Configuration;
using TestFramework.Web.Sql.Exceptions;

namespace TestFramework.Web.Sql;

/// <summary>
/// Resolves SQL configuration for a run, with an actionable error when it is absent.
/// </summary>
public static class SqlConfigResolver
{
    /// <summary>
    /// Resolves the configuration registered for a SQL identifier.
    /// </summary>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    /// <param name="identifier">The SQL identifier to resolve.</param>
    /// <returns>The resolved configuration.</returns>
    /// <exception cref="SqlConfigurationValidationException">No configuration is registered for the identifier.</exception>
    public static SqlConfig Resolve(IServiceProvider serviceProvider, string identifier)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        WebConfigStore<SqlConfig>? store = serviceProvider.GetService<WebConfigStore<SqlConfig>>();
        if (store is null)
            throw SqlConfigurationValidationException.MissingIdentifier(identifier, []);

        if (store.TryGetConfig(identifier, out SqlConfig? config) && config is not null)
            return config;

        throw SqlConfigurationValidationException.MissingIdentifier(identifier, store.Snapshot().Keys);
    }

    /// <summary>
    /// Resolves the executor for a run, defaulting to the ADO implementation.
    /// </summary>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    public static Execution.ISqlExecutor ResolveExecutor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return serviceProvider.GetService<Execution.ISqlExecutor>() ?? new Execution.AdoSqlExecutor(serviceProvider);
    }

    /// <summary>
    /// Resolves the model registry for a run, defaulting to attributes and convention only.
    /// </summary>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    public static Model.SqlModelRegistry ResolveModelRegistry(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return serviceProvider.GetService<Model.SqlModelRegistry>() ?? Model.SqlModelRegistry.CreateDefault();
    }
}
