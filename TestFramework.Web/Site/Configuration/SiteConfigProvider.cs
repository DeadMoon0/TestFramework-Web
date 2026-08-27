using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config.Configuration;
using TestFramework.Web.Configuration;
using TestFramework.Web.Site.Exceptions;

namespace TestFramework.Web.Site.Configuration;

/// <summary>
/// Reads site configuration entries from an <see cref="IConfiguration"/> source.
/// </summary>
public interface ISiteConfigProvider
{
    /// <summary>
    /// Returns every site identifier present in the configuration source.
    /// </summary>
    /// <param name="configuration">The configuration source to inspect.</param>
    string[] LoadAllSiteIdentifier(IConfiguration configuration);

    /// <summary>
    /// Reads the configuration for a single site identifier.
    /// </summary>
    /// <param name="configuration">The configuration source to read from.</param>
    /// <param name="identifier">The identifier to read.</param>
    SiteConfig LoadSiteConfig(IConfiguration configuration, string identifier);
}

/// <summary>
/// Default <see cref="ISiteConfigProvider"/> implementation that reads the <c>Site</c> section.
/// </summary>
public class DefaultSiteConfigProvider : ISiteConfigProvider
{
    /// <summary>
    /// Configuration section name for <see cref="SiteConfig"/> records.
    /// </summary>
    public const string SiteSelector = "Site";

    /// <inheritdoc />
    public string[] LoadAllSiteIdentifier(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return [.. configuration.GetSection(SiteSelector).GetChildren().Select(child => child.Key)];
    }

    /// <inheritdoc />
    public SiteConfig LoadSiteConfig(IConfiguration configuration, string identifier)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection section = configuration.GetSection(SiteSelector).GetSection(identifier);

        string? baseUrl = section.GetSection(nameof(SiteConfig.BaseUrl)).Value;
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw SiteConfigurationValidationException.InvalidValue(identifier, nameof(SiteConfig.BaseUrl), "no BaseUrl was configured");

        return new SiteConfig
        {
            BaseUrl = baseUrl,
            AllowInvalidCertificates = ParseBool(identifier, nameof(SiteConfig.AllowInvalidCertificates), section.GetSection(nameof(SiteConfig.AllowInvalidCertificates)).Value),
        };
    }

    private static bool ParseBool(string identifier, string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (bool.TryParse(value, out bool parsed))
            return parsed;

        throw SiteConfigurationValidationException.InvalidValue(identifier, propertyName, $"'{value}' is not a boolean");
    }
}

internal sealed class SiteConfigLoader(ISiteConfigProvider configProvider)
{
    internal void LoadAllConfigs(IConfiguration configuration, IServiceCollection serviceCollection)
    {
        // The same entries as resources the run's graph knows about, so anything asking where this is gets one
        // answer whether a person wrote it down or a container published it later.
        serviceCollection.AddConfigShape(new SiteShape(configProvider));
    }
}
