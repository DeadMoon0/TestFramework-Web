using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using TestFramework.Config.Configuration;
using TestFramework.Core.Environment.Graph;

namespace TestFramework.Web.Configuration;

/// <summary>
/// The <c>Api</c> section, as the run's resource graph sees it.
/// </summary>
/// <remarks>
/// <para>
/// A configured REST API is a resource somebody declared, and this is what says so: which section it lives
/// in, which kind of resource an entry describes, and which of the graph's values the entry actually holds.
/// The point is what it removes. A step that needs an address used to have to know whether the answer came
/// from a file or from a container that had just started, and ask the right one; with the entry in the graph
/// it asks the run once and never learns who answered.
/// </para>
/// <para>
/// It reads by delegating to the same provider the package's own loader uses, rather than binding the section
/// again here. Two readers for one section is two sets of validation rules, and the second one written is the
/// one that quietly accepts what the first refuses.
/// </para>
/// </remarks>
/// <param name="provider">Reads an entry, including whatever validation the caller configured.</param>
internal sealed class ApiShape(IApiConfigProvider provider) : ConfigShape<ApiConfig>
{
    /// <inheritdoc />
    public override string Section => DefaultApiConfigProvider.ApiSelector;

    /// <inheritdoc />
    public override ResourceKind Kind => WebEnvironmentResourceKinds.RestApiKind;

    /// <inheritdoc />
    public override IReadOnlyList<string> Identifiers(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadAllApiIdentifier(configuration);
    }

    /// <inheritdoc />
    public override ApiConfig Read(IConfiguration configuration, string identifier)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadApiConfig(configuration, identifier);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only what the entry holds. An address a person wrote is the same from every viewpoint - they wrote the
    /// one address the thing answers on - so it is published for both rather than for the host alone, which
    /// would leave a container-side reader with nothing. A container that later serves the same identifier
    /// produces two genuinely different addresses and overrides these.
    /// </remarks>
    public override IReadOnlyDictionary<ValueKey, string> Values(ApiConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Dictionary<ValueKey, string> values = [];

        if (config.BaseUrl is { Length: > 0 } baseUrl)
        {
            values[new ValueKey(ValueNames.BaseUrl, ResourceVantage.Host)] = baseUrl;
            values[new ValueKey(ValueNames.BaseUrl, ResourceVantage.Network)] = baseUrl;
        }

        if (config.HealthPath is { Length: > 0 } healthPath)
        {
            values[new ValueKey(nameof(ApiConfig.HealthPath))] = healthPath;
        }

        return values;
    }
}
