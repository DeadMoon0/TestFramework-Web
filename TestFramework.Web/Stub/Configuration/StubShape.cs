using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using TestFramework.Config.Configuration;
using TestFramework.Core.Environment.Graph;

namespace TestFramework.Web.Stub.Configuration;

/// <summary>
/// The <c>Stub</c> section, as the run's resource graph sees it.
/// </summary>
/// <remarks>
/// A stub is a resource like any other as far as the run is concerned: it has an address, and it has a control
/// surface at a path on that address. Reading goes through the package's own provider so the store and the
/// graph agree on what the entry said.
/// </remarks>
/// <param name="provider">Reads an entry, including whatever validation the caller configured.</param>
internal sealed class StubShape(IStubConfigProvider provider) : ConfigShape<StubConfig>
{
    /// <inheritdoc />
    public override string Section => DefaultStubConfigProvider.StubSelector;

    /// <inheritdoc />
    public override ResourceKind Kind => WebEnvironmentResourceKinds.StubKind;

    /// <inheritdoc />
    public override IReadOnlyList<string> Identifiers(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadAllStubIdentifier(configuration);
    }

    /// <inheritdoc />
    public override StubConfig Read(IConfiguration configuration, string identifier)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadStubConfig(configuration, identifier);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The admin path is published once rather than per viewpoint: it is a path on whichever address you
    /// reached the stub at, so saying it differs between viewpoints would say something untrue of it.
    /// </remarks>
    public override IReadOnlyDictionary<ValueKey, string> Values(StubConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Dictionary<ValueKey, string> values = [];

        if (config.BaseUrl is { Length: > 0 } baseUrl)
        {
            values[new ValueKey(ValueNames.BaseUrl, ResourceVantage.Host)] = baseUrl;
            values[new ValueKey(ValueNames.BaseUrl, ResourceVantage.Network)] = baseUrl;
        }

        if (config.AdminPath is { Length: > 0 } adminPath)
        {
            values[new ValueKey(WebEnvironmentResourceKinds.AdminPathValue)] = adminPath;
        }

        return values;
    }
}
