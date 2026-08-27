using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Steps;
using TestFramework.Web.Configuration;
using TestFramework.Web.Runtime;
using TestFramework.Web.Stub.Admin;
using TestFramework.Web.Stub.Exceptions;

namespace TestFramework.Web.Stub;

/// <summary>
/// Where a stub actually is, whoever supplied the answer.
/// </summary>
/// <remarks>
/// <para>
/// A configured stub has an address somebody wrote down. A containerised one gets its address when its
/// container starts, on a port the operating system chose. Both arrive as resource values, so this asks the
/// run once and callers stop having to know which happened - the branch that used to be spelled "ask the
/// environment, else read the file", and the reason a starting container used to write back into this
/// package's configuration store.
/// </para>
/// <para>
/// It takes the run rather than a service provider, which is what makes that possible: a value produced by
/// something this run started exists on the run and nowhere else. §7 of the guardrails is the general form -
/// resolving a service can never depend on which run is asking, so anything that does depend on the run has
/// to be handed it.
/// </para>
/// </remarks>
public static class StubConfigResolver
{
    /// <summary>
    /// The stub's configuration, with whatever the run knows about where it is filled in.
    /// </summary>
    /// <param name="context">The run.</param>
    /// <param name="identifier">The stub identifier to resolve.</param>
    /// <exception cref="StubConfigurationValidationException">No configuration is registered for the identifier.</exception>
    public static StubConfig Resolve(RunContext context, string identifier)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        StubConfig? declared = Declared(context.Services, identifier);
        string? address = Value(context, identifier, ValueNames.BaseUrl);
        string? adminPath = Value(context, identifier, WebEnvironmentResourceKinds.AdminPathValue);

        if (declared is null)
        {
            // Nothing wrote this stub down, so it exists only because this run started one. That is the
            // normal case for a containerised stub and it used to work by the container inventing an entry
            // in the configuration store on its way past; the run's own resources are where that belongs.
            // Everything else on the record is operational tuning with a documented default, so there is
            // nothing here to guess at.
            if (address is null)
                throw StubConfigurationValidationException.MissingIdentifier(identifier, KnownIdentifiers(context.Services));

            StubConfig provisioned = new StubConfig { BaseUrl = address };

            return adminPath is null ? provisioned : provisioned with { AdminPath = adminPath };
        }

        return declared with
        {
            BaseUrl = address ?? declared.BaseUrl,
            AdminPath = adminPath ?? declared.AdminPath,
        };
    }

    /// <summary>
    /// Creates an administration client for a stub identifier.
    /// </summary>
    /// <param name="context">The run.</param>
    /// <param name="identifier">The stub identifier to resolve.</param>
    public static StubAdminClient CreateAdminClient(RunContext context, string identifier)
    {
        StubConfig config = Resolve(context, identifier);
        return new StubAdminClient(GetClient(identifier, config), config.AdminPath);
    }

    /// <summary>
    /// What somebody wrote down, which is still the answer for a stub nothing in this run started.
    /// </summary>
    /// <remarks>
    /// Null rather than a refusal, because "nobody configured this" is not yet an error: a stub the run
    /// provisioned answers from the run's resources instead. The refusal belongs where both have been asked.
    /// </remarks>
    private static StubConfig? Declared(IServiceProvider serviceProvider, string identifier)
    {
        WebConfigStore<StubConfig>? store = serviceProvider.GetService<WebConfigStore<StubConfig>>();

        return store is not null && store.TryGetConfig(identifier, out StubConfig? config) ? config : null;
    }

    /// <summary>
    /// The stubs that were configured, so a failure can list them.
    /// </summary>
    private static IReadOnlyCollection<string> KnownIdentifiers(IServiceProvider serviceProvider)
        => serviceProvider.GetService<WebConfigStore<StubConfig>>() is { } store ? [.. store.Snapshot().Keys] : [];

    /// <summary>
    /// One value from the run, from the viewpoint of the process asking.
    /// </summary>
    /// <remarks>
    /// The host viewpoint, because this runs in the test process. Code inside the network asks for the same
    /// value name from its own viewpoint and gets the address that works there.
    /// </remarks>
    private static string? Value(RunContext context, string identifier, string valueName)
        => context.Values.TryGet(
            ValueRef.For(WebEnvironmentResourceKinds.Stub, identifier, valueName),
            ResourceVantage.Host,
            out string? value)
            ? value
            : null;

    // Clients are pooled per identifier and address, so a run that polls a stub in a loop reuses one
    // connection instead of leaking a socket per step. The pool is bounded and evicts, because a
    // container lane gives every run a new ephemeral port and therefore a new key.
    private static readonly HttpClientPool Clients = new(WebHttpClientDefaults.PoolCapacity);

    private static HttpClient GetClient(string identifier, StubConfig config)
    {
        string key = $"{identifier}|{config.BaseUrl}|{config.AllowInvalidCertificates}";
        return Clients.GetOrAdd(key, () => CreateClient(config));
    }

    private static HttpClient CreateClient(StubConfig config)
    {
        // The admin surface has no session, and this client is pooled across runs: a cookie jar here
        // could only carry one run's state into the next.
        SocketsHttpHandler handler = WebHttpClientDefaults.CreateHandler(config.AllowInvalidCertificates, useCookies: false);

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri(config.BaseUrl, UriKind.Absolute),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
