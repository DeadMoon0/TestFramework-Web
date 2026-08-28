using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;
using TestFramework.Web.Runtime;
using TestFramework.Web.Stub.Admin;
using TestFramework.Web.Stub.Configuration;

namespace TestFramework.Web.Stub;

/// <summary>
/// The pooled admin client a stub step talks to.
/// </summary>
/// <remarks>
/// <para>
/// What is left of this class after configuration moved onto the run: only the part that is actually about
/// stubs. Reading the record is <c>context.Config&lt;StubConfig&gt;(identifier)</c> now, one reader for the
/// whole family, because every package's version of it was the same twelve lines with a different noun.
/// </para>
/// <para>
/// The clients are pooled per identifier and address rather than per call, which is the one thing here that
/// needed a home of its own.
/// </para>
/// </remarks>
public static class StubConfigResolver
{
    /// <summary>
    /// Creates an administration client for a stub identifier.
    /// </summary>
    /// <param name="context">The run.</param>
    /// <param name="identifier">The stub identifier to resolve.</param>
    public static StubAdminClient CreateAdminClient(RunContext context, string identifier)
    {
        StubConfig config = context.Configured<StubConfig>(identifier);

        return new StubAdminClient(GetClient(identifier, config), config.AdminPath);
    }

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
            BaseAddress = new Uri(config.BaseUrlRequired, UriKind.Absolute),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
