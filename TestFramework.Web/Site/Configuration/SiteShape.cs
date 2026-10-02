using System;
using System.Collections.Generic;
using TestFramework.Core.Exceptions;
using Microsoft.Extensions.Configuration;
using TestFramework.Config.Configuration;
using TestFramework.Core.Environment.Graph;

namespace TestFramework.Web.Site.Configuration;

/// <summary>
/// The <c>Site</c> section, as the run's resource graph sees it.
/// </summary>
/// <remarks>
/// A site is the clearest case for describing configuration as a resource: the same identifier can be a
/// deployed address somebody wrote down or a container that starts during the run, and nothing reading it
/// should have to know which. It reads through the package's own provider, so the store and the graph cannot
/// disagree about what the entry said.
/// </remarks>
/// <param name="provider">Reads an entry, including whatever validation the caller configured.</param>
internal sealed class SiteShape(ISiteConfigProvider provider) : ConfigShape<SiteConfig>
{
    /// <inheritdoc />
    public override string Section => DefaultSiteConfigProvider.SiteSelector;

    /// <inheritdoc />
    public override ResourceKind Kind => WebEnvironmentResourceKinds.SiteKind;

    /// <inheritdoc />
    public override IReadOnlyList<string> Identifiers(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadAllSiteIdentifier(configuration);
    }

    /// <inheritdoc />
    public override SiteConfig Read(IConfiguration configuration, string identifier)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadSiteConfig(configuration, identifier);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A written address answers from both viewpoints: whoever wrote it wrote the one address the site is
    /// reachable at. A container serving the same identifier later publishes two genuinely different ones and
    /// overrides these.
    /// </remarks>
    public override IReadOnlyDictionary<ValueKey, string> Values(SiteConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Dictionary<ValueKey, string> values = [];

        if (config.BaseUrl is { Length: > 0 } baseUrl)
        {
            values[new ValueKey(ValueNames.BaseUrl, ResourceVantage.Host)] = baseUrl;
            values[new ValueKey(ValueNames.BaseUrl, ResourceVantage.Network)] = baseUrl;
        }

        values[new ValueKey(nameof(SiteConfig.AllowInvalidCertificates))] = config.AllowInvalidCertificates ? "true" : "false";

        return values;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The smallest of these in the family - a site is an address and one tolerance - which makes it the
    /// clearest example of what the pair is for: what goes out comes back, and neither direction can quietly
    /// learn a value name the other does not know.
    /// </remarks>
    public override SiteConfig Read(IReadOnlyDictionary<string, string> values, string identifier)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (!values.TryGetValue(ValueNames.BaseUrl, out string? baseUrl) || string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new FrameworkConfigurationException(
                $"The run has no address for site '{identifier}'.")
            {
                RecoverySteps = [
                    $"Set 'Site:{identifier}:BaseUrl', or include the site definition that serves it.",
                ],
                AvailableOptions = [.. values.Keys],
            };
        }

        SiteConfig config = new SiteConfig { BaseUrl = baseUrl };

        if (values.TryGetValue(nameof(SiteConfig.AllowInvalidCertificates), out string? allowInvalid))
        {
            config = config with
            {
                AllowInvalidCertificates = bool.TryParse(allowInvalid, out bool parsed)
                    ? parsed
                    : throw new FrameworkConfigurationException(
                        $"The run holds '{allowInvalid}' as {nameof(SiteConfig.AllowInvalidCertificates)} for site '{identifier}', which cannot be read as 'true' or 'false'.")
                    {
                        RecoverySteps = [
                            $"Correct 'Site:{identifier}:{nameof(SiteConfig.AllowInvalidCertificates)}', or the environment component that publishes it.",
                        ],
                    },
            };
        }

        return config;
    }
}
