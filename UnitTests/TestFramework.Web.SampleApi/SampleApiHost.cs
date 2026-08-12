using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestFramework.Web.SampleApi;

/// <summary>
/// Builds the sample API used to exercise the web module against a real HTTP stack.
/// </summary>
/// <remarks>
/// Every endpoint exists to drive one framework behaviour, not to model a domain: query binding,
/// route substitution, created-resource headers, problem responses, slow responses, warmup
/// flakiness and credential handling.
/// </remarks>
public static class SampleApiHost
{
    /// <summary>
    /// Header name accepted by the secured endpoint.
    /// </summary>
    public const string ApiKeyHeaderName = "x-api-key";

    /// <summary>
    /// Key value accepted by the secured endpoint.
    /// </summary>
    public const string ApiKeyValue = "sample-key";

    /// <summary>
    /// Bearer token accepted by the secured endpoint.
    /// </summary>
    public const string BearerTokenValue = "sample-token";

    /// <summary>
    /// Creates the sample application bound to an ephemeral loopback port.
    /// </summary>
    /// <returns>The application, not yet started.</returns>
    public static WebApplication Create()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<SampleItemStore>();

        WebApplication app = builder.Build();
        MapEndpoints(app);
        return app;
    }

    private static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

        app.MapGet("/api/items", (SampleItemStore store, int? take) =>
        {
            IEnumerable<SampleItem> items = store.All();
            if (take is { } limit)
                items = items.Take(limit);

            return Results.Ok(items.ToArray());
        });

        app.MapGet("/api/items/{id}", (SampleItemStore store, string id) =>
            store.TryGet(id, out SampleItem? item) && item is not null
                ? Results.Ok(item)
                : Results.NotFound(new { id }));

        app.MapPost("/api/items", (SampleItemStore store, CreateSampleItem payload) =>
        {
            SampleItem created = store.Add(payload);
            return Results.Created($"/api/items/{created.Id}", created);
        });

        // Echoes the request back so tests can assert what actually went over the wire.
        app.MapGet("/api/echo", (HttpRequest request) => Results.Ok(new EchoResponse(
            request.Method,
            request.Path.Value ?? string.Empty,
            request.QueryString.Value ?? string.Empty,
            request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()!, StringComparer.OrdinalIgnoreCase),
            request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()!, StringComparer.OrdinalIgnoreCase))));

        app.MapPost("/api/echo", async (HttpRequest request) =>
        {
            using System.IO.StreamReader reader = new(request.Body);
            string body = await reader.ReadToEndAsync().ConfigureAwait(false);
            return Results.Ok(new EchoBodyResponse(request.ContentType, body));
        });

        app.MapGet("/api/problem", () => Results.Problem(
            title: "Sample problem",
            detail: "The sample API always fails this endpoint on purpose.",
            statusCode: StatusCodes.Status400BadRequest));

        app.MapGet("/api/slow", async (int? delayMs, CancellationToken cancellationToken) =>
        {
            await Task.Delay(delayMs ?? 1000, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { delayed = true });
        });

        // Answers 404 for the first N calls per key, then succeeds. Drives the warmup-retry rule.
        app.MapGet("/api/flaky", (SampleItemStore store, string key, int? failures) =>
        {
            int attempt = store.NextAttempt(key);
            return attempt <= (failures ?? 1)
                ? Results.NotFound(new { key, attempt })
                : Results.Ok(new { key, attempt });
        });

        app.MapGet("/api/secure", (HttpRequest request) =>
        {
            string? apiKey = request.Headers[ApiKeyHeaderName].FirstOrDefault();
            string? authorization = request.Headers.Authorization.FirstOrDefault();

            bool authorized = string.Equals(apiKey, ApiKeyValue, StringComparison.Ordinal)
                || string.Equals(authorization, $"Bearer {BearerTokenValue}", StringComparison.Ordinal);

            return authorized
                ? Results.Ok(new { authorized = true })
                : Results.Unauthorized();
        });

        app.MapGet("/api/status/{code}", (int code) => Results.StatusCode(code));
    }

    /// <summary>
    /// Reads the base URL the application actually bound to.
    /// </summary>
    /// <param name="app">The started application.</param>
    /// <returns>The absolute base URL including the assigned port.</returns>
    public static string GetBaseUrl(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        string? address = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("The sample API has no bound address. Start it before reading the base URL.");

        return address.EndsWith('/') ? address : address + "/";
    }

    private sealed record EchoResponse(
        string Method,
        string Path,
        string QueryString,
        Dictionary<string, string[]> Query,
        Dictionary<string, string[]> Headers);

    private sealed record EchoBodyResponse(string? ContentType, string Body);

    private sealed class SampleItemStore
    {
        private readonly ConcurrentDictionary<string, SampleItem> _items = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
        private int _nextId = 1;

        public SampleItemStore()
        {
            Add(new CreateSampleItem("first", 10));
            Add(new CreateSampleItem("second", 20));
            Add(new CreateSampleItem("third", 30));
        }

        public IEnumerable<SampleItem> All() => _items.Values.OrderBy(item => item.Id, StringComparer.Ordinal);

        public bool TryGet(string id, out SampleItem? item) => _items.TryGetValue(id, out item);

        public SampleItem Add(CreateSampleItem payload)
        {
            string id = Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);
            SampleItem created = new(id, payload.Name, payload.Quantity);
            _items[id] = created;
            return created;
        }

        public int NextAttempt(string key) => _attempts.AddOrUpdate(key, 1, (_, current) => current + 1);
    }
}
