using System;
using System.Collections.Generic;
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

        return values;
    }
}
