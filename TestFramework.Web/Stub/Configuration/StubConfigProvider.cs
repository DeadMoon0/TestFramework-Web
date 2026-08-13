using System;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Configuration;
using TestFramework.Web.Stub.Exceptions;

namespace TestFramework.Web.Stub.Configuration;

/// <summary>
/// Reads stub configuration entries from an <see cref="IConfiguration"/> source.
/// </summary>
public interface IStubConfigProvider
{
    /// <summary>
    /// Returns every stub identifier present in the configuration source.
    /// </summary>
    /// <param name="configuration">The configuration source to inspect.</param>
    string[] LoadAllStubIdentifier(IConfiguration configuration);

    /// <summary>
    /// Reads the configuration for a single stub identifier.
    /// </summary>
    /// <param name="configuration">The configuration source to read from.</param>
    /// <param name="identifier">The identifier to read.</param>
    StubConfig LoadStubConfig(IConfiguration configuration, string identifier);
}

/// <summary>
/// Default <see cref="IStubConfigProvider"/> implementation that reads the <c>Stub</c> section.
/// </summary>
public class DefaultStubConfigProvider : IStubConfigProvider
{
    /// <summary>
    /// Configuration section name for <see cref="StubConfig"/> records.
    /// </summary>
    public const string StubSelector = "Stub";

    /// <inheritdoc />
    public string[] LoadAllStubIdentifier(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return [.. configuration.GetSection(StubSelector).GetChildren().Select(child => child.Key)];
    }

    /// <inheritdoc />
    public StubConfig LoadStubConfig(IConfiguration configuration, string identifier)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection section = configuration.GetSection(StubSelector).GetSection(identifier);

        string? baseUrl = section.GetSection(nameof(StubConfig.BaseUrl)).Value;
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw StubConfigurationValidationException.InvalidValue(identifier, nameof(StubConfig.BaseUrl), "no BaseUrl was configured");

        StubConfig config = new()
        {
            BaseUrl = baseUrl,
            AllowInvalidCertificates = ParseBool(identifier, nameof(StubConfig.AllowInvalidCertificates), section.GetSection(nameof(StubConfig.AllowInvalidCertificates)).Value),
        };

        if (section.GetSection(nameof(StubConfig.AdminPath)).Value is { Length: > 0 } adminPath)
            config = config with { AdminPath = adminPath };

        if (ParseTimeSpan(identifier, nameof(StubConfig.PollInterval), section.GetSection(nameof(StubConfig.PollInterval)).Value) is { } pollInterval)
            config = config with { PollInterval = pollInterval };

        return config;
    }

    private static bool ParseBool(string identifier, string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (bool.TryParse(value, out bool parsed))
            return parsed;

        throw StubConfigurationValidationException.InvalidValue(identifier, propertyName, $"'{value}' is not a boolean");
    }

    private static TimeSpan? ParseTimeSpan(string identifier, string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsed) && parsed > TimeSpan.Zero)
            return parsed;

        throw StubConfigurationValidationException.InvalidValue(identifier, propertyName, $"'{value}' is not a positive time span. Use a value such as '00:00:00.250'");
    }
}

internal sealed class StubConfigLoader(IStubConfigProvider configProvider)
{
    internal void LoadAllConfigs(IConfiguration configuration, IServiceCollection serviceCollection)
    {
        // Registered even when the section is absent, so an environment can hydrate identifiers at
        // run time.
        WebConfigStore<StubConfig> store = new();
        serviceCollection.AddSingleton(store);

        foreach (string identifier in configProvider.LoadAllStubIdentifier(configuration))
            store.AddConfig(identifier, configProvider.LoadStubConfig(configuration, identifier));
    }
}
