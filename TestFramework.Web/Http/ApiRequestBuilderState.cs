using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using TestFramework.Core.Variables;
using TestFramework.Web.Auth;

namespace TestFramework.Web.Http;

/// <summary>
/// Accumulates the variable-backed parts of a request while the fluent builder runs.
/// </summary>
internal sealed class ApiRequestBuilderState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly List<(string Name, VariableReference<string> Value)> _routeValues = [];
    private readonly List<(string Name, VariableReference<string> Value)> _query = [];
    private readonly List<(VariableReference<string> Key, VariableReference<string> Value)> _headers = [];
    private readonly List<(VariableReference<string> Key, VariableReference<string[]> Values)> _multiHeaders = [];
    private readonly List<VariableReference<Dictionary<string, string>>> _headerDictionaries = [];
    private readonly List<VariableReferenceGeneric> _inputs = [];

    private HttpMethod _method = HttpMethod.Get;
    private VariableReference<string> _path = Var.Const(string.Empty);
    private Func<VariableStore, string?>? _bodyTextFactory;
    private VariableReference<byte[]>? _bodyBytes;
    private string? _contentType;
    private IApiAuthenticationProvider? _authOverride;

    internal void SetEndpoint(HttpMethod method, VariableReference<string> path)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        _method = method;
        _path = path;
        Track(path);
    }

    internal void AddRouteValue(string name, VariableReference<string> value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _routeValues.Add((name, value));
        Track(value);
    }

    internal void AddQuery(string name, VariableReference<string> value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _query.Add((name, value));
        Track(value);
    }

    internal void AddHeader(VariableReference<string> key, VariableReference<string> value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _headers.Add((key, value));
        Track(key);
        Track(value);
    }

    internal void AddHeader(VariableReference<string> key, VariableReference<string[]> values)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(values);
        _multiHeaders.Add((key, values));
        Track(key);
        Track(values);
    }

    internal void AddHeaderDictionary(VariableReference<Dictionary<string, string>> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        _headerDictionaries.Add(headers);
        Track(headers);
    }

    internal void SetBody(VariableReference<string> text, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(text);
        _bodyBytes = null;
        _bodyTextFactory = store => text.GetValue(store);
        _contentType = contentType;
        Track(text);
    }

    internal void SetBody(VariableReference<byte[]> data, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(data);
        _bodyTextFactory = null;
        _bodyBytes = data;
        _contentType = contentType;
        Track(data);
    }

    internal void SetJsonBody<TValue>(VariableReference<TValue> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _bodyBytes = null;
        _bodyTextFactory = store => JsonSerializer.Serialize(value.GetValue(store), JsonOptions);
        _contentType = "application/json; charset=utf-8";
        Track(value);
    }

    internal void SetAuthOverride(IApiAuthenticationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _authOverride = provider;
    }

    internal ComposedRequestVariable BuildVariable()
        => new(_method, _path, _routeValues, _query, _headers, _multiHeaders, _headerDictionaries, _bodyTextFactory, _bodyBytes, _contentType, _authOverride, _inputs);

    private void Track(VariableReferenceGeneric reference)
    {
        if (reference.HasIdentifier)
            _inputs.Add(reference);
    }
}

/// <summary>
/// Variable reference that resolves every part of a request into a <see cref="HttpRequestSpec"/>.
/// </summary>
internal sealed class ComposedRequestVariable(
    HttpMethod method,
    VariableReference<string> path,
    List<(string Name, VariableReference<string> Value)> routeValues,
    List<(string Name, VariableReference<string> Value)> query,
    List<(VariableReference<string> Key, VariableReference<string> Value)> headers,
    List<(VariableReference<string> Key, VariableReference<string[]> Values)> multiHeaders,
    List<VariableReference<Dictionary<string, string>>> headerDictionaries,
    Func<VariableStore, string?>? bodyTextFactory,
    VariableReference<byte[]>? bodyBytes,
    string? contentType,
    IApiAuthenticationProvider? authOverride,
    List<VariableReferenceGeneric> inputs) : VariableReference<HttpRequestSpec>
{
    public override bool RequireImmutability => false;

    public override bool HasIdentifier => false;

    public override VariableIdentifier? Identifier => null;

    /// <summary>
    /// The identifier-backed variable references this request depends on.
    /// </summary>
    internal IReadOnlyList<VariableReferenceGeneric> Inputs => inputs;

    public override HttpRequestSpec? GetValue(VariableStore store)
    {
        Dictionary<string, string> resolvedRouteValues = new(StringComparer.Ordinal);
        foreach ((string name, VariableReference<string> value) in routeValues)
            resolvedRouteValues[name] = value.GetRequiredValue(store, $"route value '{name}'");

        List<KeyValuePair<string, string>> resolvedQuery = [];
        foreach ((string name, VariableReference<string> value) in query)
        {
            string? resolved = value.GetValue(store);
            if (resolved is not null)
                resolvedQuery.Add(new KeyValuePair<string, string>(name, resolved));
        }

        List<KeyValuePair<string, string[]>> resolvedHeaders = [];
        foreach ((VariableReference<string> key, VariableReference<string> value) in headers)
            resolvedHeaders.Add(new KeyValuePair<string, string[]>(key.GetRequiredValue(store, "header name"), [value.GetRequiredValue(store, "header value")]));

        foreach ((VariableReference<string> key, VariableReference<string[]> values) in multiHeaders)
            resolvedHeaders.Add(new KeyValuePair<string, string[]>(key.GetRequiredValue(store, "header name"), values.GetRequiredValue(store, "header values")));

        foreach (VariableReference<Dictionary<string, string>> dictionary in headerDictionaries)
        {
            Dictionary<string, string>? resolvedDictionary = dictionary.GetValue(store);
            if (resolvedDictionary is null)
                continue;

            foreach ((string key, string value) in resolvedDictionary)
                resolvedHeaders.Add(new KeyValuePair<string, string[]>(key, [value]));
        }

        return new HttpRequestSpec(
            method,
            path.GetRequiredValue(store, "request path"),
            resolvedRouteValues,
            resolvedQuery,
            resolvedHeaders,
            bodyTextFactory?.Invoke(store),
            bodyBytes?.GetValue(store),
            contentType,
            authOverride);
    }

    public override VariableReference<TNew> Transform<TNew>(Func<HttpRequestSpec?, TNew?> transform) where TNew : default
        => throw new NotSupportedException("A composed API request cannot be transformed. Build the request from variables instead.");

    public override VariableReference<TNew> Transform<TNew>(Func<HttpRequestSpec?, object?[], TNew?> transform, params VariableReferenceGeneric[] variables) where TNew : default
        => throw new NotSupportedException("A composed API request cannot be transformed. Build the request from variables instead.");
}
