using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TestFramework.Web.Stub.Mappings;

/// <summary>
/// Declares what a stub answers.
/// </summary>
/// <remarks>
/// Mappings are tried in declaration order, so the first one that matches wins. Declare the specific
/// case before the general one.
/// </remarks>
public sealed class StubMappingBuilder
{
    private readonly List<StubMapping> _mappings = [];

    /// <summary>
    /// Starts a mapping for a <c>GET</c> request.
    /// </summary>
    /// <param name="path">The path, where <c>*</c> matches any run of characters.</param>
    public StubRequestBuilder OnGet(string path) => On(HttpMethod.Get, path);

    /// <summary>
    /// Starts a mapping for a <c>POST</c> request.
    /// </summary>
    /// <param name="path">The path, where <c>*</c> matches any run of characters.</param>
    public StubRequestBuilder OnPost(string path) => On(HttpMethod.Post, path);

    /// <summary>
    /// Starts a mapping for a <c>PUT</c> request.
    /// </summary>
    /// <param name="path">The path, where <c>*</c> matches any run of characters.</param>
    public StubRequestBuilder OnPut(string path) => On(HttpMethod.Put, path);

    /// <summary>
    /// Starts a mapping for a <c>DELETE</c> request.
    /// </summary>
    /// <param name="path">The path, where <c>*</c> matches any run of characters.</param>
    public StubRequestBuilder OnDelete(string path) => On(HttpMethod.Delete, path);

    /// <summary>
    /// Starts a mapping for any method.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path, where <c>*</c> matches any run of characters.</param>
    public StubRequestBuilder On(HttpMethod method, string path)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new StubRequestBuilder(this, method.Method, path.StartsWith('/') ? path : $"/{path}");
    }

    /// <summary>
    /// Returns the declared mappings, in declaration order.
    /// </summary>
    public IReadOnlyList<StubMapping> Build() => [.. _mappings];

    internal StubMappingBuilder Add(StubMapping mapping)
    {
        _mappings.Add(mapping);
        return this;
    }

    internal int NextPriority => _mappings.Count + 1;
}

/// <summary>
/// Narrows which requests a mapping matches, then declares the answer.
/// </summary>
public sealed class StubRequestBuilder
{
    private readonly StubMappingBuilder _owner;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _query = new(StringComparer.Ordinal);
    private readonly string _method;
    private readonly string _path;
    private string? _bodyContains;
    private int? _priority;

    internal StubRequestBuilder(StubMappingBuilder owner, string method, string path)
    {
        _owner = owner;
        _method = method;
        _path = path;
    }

    /// <summary>
    /// Requires a header to be present, with any value.
    /// </summary>
    /// <param name="name">The header name.</param>
    public StubRequestBuilder WithHeader(string name) => WithHeader(name, "*");

    /// <summary>
    /// Requires a header to have a value.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The expected value, where <c>*</c> matches any run of characters.</param>
    public StubRequestBuilder WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _headers[name] = value;
        return this;
    }

    /// <summary>
    /// Requires a query parameter to have a value.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The expected value, where <c>*</c> matches any run of characters.</param>
    public StubRequestBuilder WithQuery(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _query[name] = value;
        return this;
    }

    /// <summary>
    /// Requires the request body to contain a text.
    /// </summary>
    /// <param name="text">The text the body must contain.</param>
    public StubRequestBuilder WithBodyContaining(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _bodyContains = text;
        return this;
    }

    /// <summary>
    /// Overrides which mapping wins when several match. Lower wins.
    /// </summary>
    /// <param name="priority">The priority.</param>
    public StubRequestBuilder WithPriority(int priority)
    {
        _priority = priority;
        return this;
    }

    /// <summary>
    /// Answers with a JSON body.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="body">The body, serialized to JSON.</param>
    /// <param name="delay">How long to wait before answering.</param>
    /// <param name="useTemplating">
    /// Whether the body may quote the request back, for example <c>{{request.body.amount}}</c>.
    /// </param>
    public StubMappingBuilder RespondJson(HttpStatusCode statusCode, object body, TimeSpan? delay = null, bool useTemplating = false)
    {
        ArgumentNullException.ThrowIfNull(body);

        JsonNode node = JsonSerializer.SerializeToNode(body)
            ?? throw new ArgumentException("The response body serialized to nothing.", nameof(body));

        return Respond(new StubResponse(
            (int)statusCode,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "application/json" },
            null,
            node,
            delay,
            useTemplating));
    }

    /// <summary>
    /// Answers with a literal body.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="body">The body, sent as it is.</param>
    /// <param name="contentType">The content type.</param>
    /// <param name="delay">How long to wait before answering.</param>
    public StubMappingBuilder RespondText(HttpStatusCode statusCode, string body, string contentType = "text/plain", TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        return Respond(new StubResponse(
            (int)statusCode,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = contentType },
            body,
            null,
            delay,
            false));
    }

    /// <summary>
    /// Answers with a status code and no body.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="delay">How long to wait before answering.</param>
    public StubMappingBuilder RespondStatus(HttpStatusCode statusCode, TimeSpan? delay = null)
        => Respond(new StubResponse((int)statusCode, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null, null, delay, false));

    /// <summary>
    /// Answers with a fully described response.
    /// </summary>
    /// <param name="response">The answer.</param>
    public StubMappingBuilder Respond(StubResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return _owner.Add(new StubMapping(
            _method,
            _path,
            _headers,
            _query,
            _bodyContains,
            _priority ?? _owner.NextPriority,
            response));
    }
}
