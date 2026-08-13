using System;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Configuration;
using TestFramework.Web.Sql.Exceptions;

namespace TestFramework.Web.Sql.Configuration;

/// <summary>
/// Reads SQL configuration entries from an <see cref="IConfiguration"/> source.
/// </summary>
public interface ISqlConfigProvider
{
    /// <summary>
    /// Returns every SQL identifier present in the configuration source.
    /// </summary>
    /// <param name="configuration">The configuration source to inspect.</param>
    string[] LoadAllSqlIdentifier(IConfiguration configuration);

    /// <summary>
    /// Reads the configuration for a single SQL identifier.
    /// </summary>
    /// <param name="configuration">The configuration source to read from.</param>
    /// <param name="identifier">The identifier to read.</param>
    SqlConfig LoadSqlConfig(IConfiguration configuration, string identifier);
}

/// <summary>
/// Default <see cref="ISqlConfigProvider"/> implementation that reads the <c>Sql</c> section.
/// </summary>
public class DefaultSqlConfigProvider : ISqlConfigProvider
{
    /// <summary>
    /// Configuration section name for <see cref="SqlConfig"/> records.
    /// </summary>
    public const string SqlSelector = "Sql";

    /// <inheritdoc />
    public string[] LoadAllSqlIdentifier(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return [.. configuration.GetSection(SqlSelector).GetChildren().Select(child => child.Key)];
    }

    /// <inheritdoc />
    public SqlConfig LoadSqlConfig(IConfiguration configuration, string identifier)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection section = configuration.GetSection(SqlSelector).GetSection(identifier);

        SqlConfig config = new()
        {
            ConnectionString = section.GetSection(nameof(SqlConfig.ConnectionString)).Value,
            Server = section.GetSection(nameof(SqlConfig.Server)).Value,
            Database = section.GetSection(nameof(SqlConfig.Database)).Value,
            IntegratedSecurity = ParseBool(identifier, nameof(SqlConfig.IntegratedSecurity), section.GetSection(nameof(SqlConfig.IntegratedSecurity)).Value),
            UserName = section.GetSection(nameof(SqlConfig.UserName)).Value,
            Password = section.GetSection(nameof(SqlConfig.Password)).Value,
            TrustServerCertificate = ParseBool(identifier, nameof(SqlConfig.TrustServerCertificate), section.GetSection(nameof(SqlConfig.TrustServerCertificate)).Value),
            Encrypt = ParseOptionalBool(identifier, nameof(SqlConfig.Encrypt), section.GetSection(nameof(SqlConfig.Encrypt)).Value),
            ConnectTimeout = ParseTimeSpan(identifier, nameof(SqlConfig.ConnectTimeout), section.GetSection(nameof(SqlConfig.ConnectTimeout)).Value),
            CommandTimeout = ParseTimeSpan(identifier, nameof(SqlConfig.CommandTimeout), section.GetSection(nameof(SqlConfig.CommandTimeout)).Value),
        };

        if (string.IsNullOrWhiteSpace(config.ConnectionString) && string.IsNullOrWhiteSpace(config.Server))
            throw SqlConfigurationValidationException.InvalidValue(identifier, nameof(SqlConfig.Server), "neither ConnectionString nor Server was configured");

        return config;
    }

    private static bool ParseBool(string identifier, string propertyName, string? value)
        => ParseOptionalBool(identifier, propertyName, value) ?? false;

    private static bool? ParseOptionalBool(string identifier, string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (bool.TryParse(value, out bool parsed))
            return parsed;

        throw SqlConfigurationValidationException.InvalidValue(identifier, propertyName, $"'{value}' is not a boolean");
    }

    private static TimeSpan? ParseTimeSpan(string identifier, string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsed) && parsed > TimeSpan.Zero)
            return parsed;

        throw SqlConfigurationValidationException.InvalidValue(identifier, propertyName, $"'{value}' is not a positive time span. Use a value such as '00:00:30'");
    }
}

internal sealed class SqlConfigLoader(ISqlConfigProvider configProvider)
{
    internal void LoadAllConfigs(IConfiguration configuration, IServiceCollection serviceCollection)
    {
        // Registered even when the section is absent, so an environment can hydrate identifiers at
        // run time.
        WebConfigStore<SqlConfig> store = new();
        serviceCollection.AddSingleton(store);

        foreach (string identifier in configProvider.LoadAllSqlIdentifier(configuration))
            store.AddConfig(identifier, configProvider.LoadSqlConfig(configuration, identifier));
    }
}
