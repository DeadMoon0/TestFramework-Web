using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Steps;
using TestFramework.Web.Exceptions;

namespace TestFramework.Web.Configuration;

/// <summary>
/// Where an API actually is, whoever supplied the answer.
/// </summary>
/// <remarks>
/// <para>
/// A configured API has an address somebody wrote down. One this run started in a container gets its address
/// when the container starts, on a port the operating system chose. Both arrive as resource values, so this
/// asks the run once and callers stop having to know which happened - the branch that used to be spelled "ask
/// the environment, else read the file", and the reason a starting container used to write back into this
/// package's configuration store.
/// </para>
/// <para>
/// It takes the run rather than a service provider, which is what makes that possible: a value produced by
/// something this run started exists on the run and nowhere else. §7 of the guardrails is the general form -
/// resolving a service can never depend on which run is asking, so anything that does depend on the run has
/// to be handed it.
/// </para>
/// </remarks>
public static class ApiConfigResolver
{
    /// <summary>
    /// The API's configuration, with whatever the run knows about where it is filled in.
    /// </summary>
    /// <param name="context">The run.</param>
    /// <param name="identifier">The API identifier to resolve.</param>
    /// <returns>The resolved configuration.</returns>
    /// <exception cref="ApiConfigurationValidationException">Neither the run nor any configuration entry knows this API.</exception>
    public static ApiConfig Resolve(RunContext context, string identifier)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        ApiConfig? declared = Declared(context.Services, identifier);
        string? address = Value(context, identifier, ValueNames.BaseUrl);
        string? healthPath = Value(context, identifier, nameof(ApiConfig.HealthPath));

        if (declared is null)
        {
            // Nothing wrote this API down, so it exists only because this run started one - the normal case
            // for a containerised API, whose address does not exist until it is up. Everything else on the
            // record is operational tuning with a documented default, and authentication that nobody
            // configured is authentication the API does not want.
            if (address is null)
                throw ApiConfigurationValidationException.MissingIdentifier(identifier, KnownIdentifiers(context.Services));

            ApiConfig provisioned = new ApiConfig { BaseUrl = address };

            return healthPath is null ? provisioned : provisioned with { HealthPath = healthPath };
        }

        return declared with
        {
            BaseUrl = address ?? declared.BaseUrl,
            HealthPath = healthPath ?? declared.HealthPath,
        };
    }

    /// <summary>
    /// What somebody wrote down, which is still the answer for an API nothing in this run started.
    /// </summary>
    /// <remarks>
    /// Null rather than a refusal, because "nobody configured this" is not yet an error: an API the run
    /// provisioned answers from the run's resources instead. The refusal belongs where both have been asked.
    /// </remarks>
    private static ApiConfig? Declared(IServiceProvider serviceProvider, string identifier)
    {
        WebConfigStore<ApiConfig>? store = serviceProvider.GetService<WebConfigStore<ApiConfig>>();

        return store is not null && store.TryGetConfig(identifier, out ApiConfig? config) ? config : null;
    }

    /// <summary>
    /// The APIs that were configured, so a failure can list them.
    /// </summary>
    private static IReadOnlyCollection<string> KnownIdentifiers(IServiceProvider serviceProvider)
        => serviceProvider.GetService<WebConfigStore<ApiConfig>>() is { } store ? [.. store.Snapshot().Keys] : [];

    /// <summary>
    /// One value from the run, from the viewpoint of the process asking.
    /// </summary>
    /// <remarks>
    /// The host viewpoint, because this runs in the test process. Code inside the network asks for the same
    /// value name from its own viewpoint and gets the address that works there.
    /// </remarks>
    private static string? Value(RunContext context, string identifier, string valueName)
        => context.Values.TryGet(
            ValueRef.For(WebEnvironmentResourceKinds.RestApi, identifier, valueName),
            ResourceVantage.Host,
            out string? value)
            ? value
            : null;
}
