using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Auth;
using TestFramework.Web.Configuration;
using TestFramework.Web.Http;

namespace TestFramework.Web.Runtime;

/// <summary>
/// Creates the runtime components used by web triggers.
/// </summary>
/// <remarks>
/// Register a replacement to redirect every API call in a run, for example to an in-process test
/// host or to a recording sender.
/// </remarks>
public interface IWebComponentFactory
{
    /// <summary>
    /// Creates or reuses the sender for an API identifier.
    /// </summary>
    /// <param name="identifier">The API identifier being called.</param>
    /// <param name="config">The resolved configuration for that identifier.</param>
    IHttpSender CreateSender(string identifier, ApiConfig config);
}

/// <summary>
/// Resolves the active <see cref="IWebComponentFactory"/> from a service provider.
/// </summary>
public static class WebComponentFactoryExtensions
{
    /// <summary>
    /// Returns the registered factory, or the shared default when none is registered.
    /// </summary>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    public static IWebComponentFactory GetWebComponentFactory(this IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return serviceProvider.GetService<IWebComponentFactory>() ?? DefaultWebComponentFactory.Instance;
    }
}

/// <summary>
/// Default factory that talks to real endpoints over pooled <see cref="HttpClient"/> instances.
/// </summary>
public sealed class DefaultWebComponentFactory : IWebComponentFactory
{
    private static readonly ConcurrentDictionary<string, Lazy<HttpClient>> Clients = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the shared factory instance.
    /// </summary>
    public static DefaultWebComponentFactory Instance { get; } = new();

    /// <inheritdoc />
    public IHttpSender CreateSender(string identifier, ApiConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Clients are pooled per identifier and per connection-relevant settings so repeated steps
        // reuse connections, while a run that rewrites the endpoint still gets a fresh client.
        string key = string.Create(
            CultureInfo.InvariantCulture,
            $"{identifier}|{config.BaseUrl}|{config.Auth}|{config.AllowInvalidCertificates}|{config.RequestTimeout}|{config.UseCookies}");

        HttpClient client = Clients.GetOrAdd(key, _ => new Lazy<HttpClient>(() => CreateClient(config), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        return new HttpClientSender(client);
    }

    private static HttpClient CreateClient(ApiConfig config)
    {
        // Cookies are off unless asked for: the client is pooled, so its jar would otherwise outlive
        // the run that filled it and replay a session onto the next one.
        HttpClientHandler handler = new()
        {
            UseDefaultCredentials = config.Auth == ApiAuthMode.Negotiate,
            UseCookies = config.UseCookies,
        };

        if (config.AllowInvalidCertificates)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        HttpClient client = new(handler, disposeHandler: true);

        // Transport timeouts stay opt-in: without one the step timeout is the single source of truth.
        if (config.RequestTimeout is { } timeout)
            client.Timeout = timeout;
        else
            client.Timeout = Timeout.InfiniteTimeSpan;

        return client;
    }

    private sealed class HttpClientSender(HttpClient client) : IHttpSender
    {
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
            => client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }
}
