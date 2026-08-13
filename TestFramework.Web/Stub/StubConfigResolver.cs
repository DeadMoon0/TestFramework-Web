using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Configuration;
using TestFramework.Web.Stub.Admin;
using TestFramework.Web.Stub.Exceptions;

namespace TestFramework.Web.Stub;

/// <summary>
/// Resolves stub configuration for a run, with an actionable error when it is absent.
/// </summary>
public static class StubConfigResolver
{
    /// <summary>
    /// Resolves the configuration registered for a stub identifier.
    /// </summary>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    /// <param name="identifier">The stub identifier to resolve.</param>
    /// <exception cref="StubConfigurationValidationException">No configuration is registered for the identifier.</exception>
    public static StubConfig Resolve(IServiceProvider serviceProvider, string identifier)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        WebConfigStore<StubConfig>? store = serviceProvider.GetService<WebConfigStore<StubConfig>>();
        if (store is null)
            throw StubConfigurationValidationException.MissingIdentifier(identifier, []);

        if (store.TryGetConfig(identifier, out StubConfig? config) && config is not null)
            return config;

        throw StubConfigurationValidationException.MissingIdentifier(identifier, store.Snapshot().Keys);
    }

    /// <summary>
    /// Creates an administration client for a stub identifier.
    /// </summary>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    /// <param name="identifier">The stub identifier to resolve.</param>
    public static StubAdminClient CreateAdminClient(IServiceProvider serviceProvider, string identifier)
    {
        StubConfig config = Resolve(serviceProvider, identifier);
        return new StubAdminClient(GetClient(identifier, config), config.AdminPath);
    }

    // Clients are pooled per identifier and address, so a run that polls a stub in a loop reuses one
    // connection instead of leaking a socket per step.
    private static readonly ConcurrentDictionary<string, Lazy<HttpClient>> Clients = new(StringComparer.Ordinal);

    private static HttpClient GetClient(string identifier, StubConfig config)
    {
        string key = $"{identifier}|{config.BaseUrl}|{config.AllowInvalidCertificates}";
        return Clients.GetOrAdd(key, _ => new Lazy<HttpClient>(() => CreateClient(config), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static HttpClient CreateClient(StubConfig config)
    {
        HttpClientHandler handler = new();
        if (config.AllowInvalidCertificates)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri(config.BaseUrl, UriKind.Absolute),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
