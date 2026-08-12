using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using TestFramework.Web.Auth;
using TestFramework.Web.Exceptions;

namespace TestFramework.Web.Http;

/// <summary>
/// Fully resolved description of a single API request.
/// </summary>
/// <remarks>
/// Produced from variable references when the step executes, so every part of the request can be
/// data-driven. URI composition, route substitution and escaping all happen here rather than in
/// the trigger, which keeps one rule for how a request becomes a URI.
/// </remarks>
public sealed class HttpRequestSpec
{
    private readonly Dictionary<string, string> _routeValues;
    private readonly List<KeyValuePair<string, string>> _query;
    private readonly List<KeyValuePair<string, string[]>> _headers;

    internal HttpRequestSpec(
        HttpMethod method,
        string path,
        Dictionary<string, string> routeValues,
        List<KeyValuePair<string, string>> query,
        List<KeyValuePair<string, string[]>> headers,
        string? bodyText,
        byte[]? bodyBytes,
        string? contentType,
        IApiAuthenticationProvider? authOverride)
    {
        Method = method;
        Path = path;
        _routeValues = routeValues;
        _query = query;
        _headers = headers;
        BodyText = bodyText;
        BodyBytes = bodyBytes;
        ContentType = contentType;
        AuthOverride = authOverride;
    }

    /// <summary>
    /// The HTTP method of the request.
    /// </summary>
    public HttpMethod Method { get; }

    /// <summary>
    /// The request path relative to the configured base URL, with unsubstituted route tokens.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Route token values substituted into <see cref="Path"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> RouteValues => _routeValues;

    /// <summary>
    /// Query string values appended to the request.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Query => _query;

    /// <summary>
    /// Request headers applied to the outgoing message.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string[]>> Headers => _headers;

    /// <summary>
    /// Text body, when one was supplied.
    /// </summary>
    public string? BodyText { get; }

    /// <summary>
    /// Binary body, when one was supplied.
    /// </summary>
    public byte[]? BodyBytes { get; }

    /// <summary>
    /// Content type of the body, when one was supplied.
    /// </summary>
    public string? ContentType { get; }

    /// <summary>
    /// Per-request authentication that replaces the configured mode.
    /// </summary>
    public IApiAuthenticationProvider? AuthOverride { get; }

    /// <summary>
    /// Composes the absolute request URI from a base URL, the path, route values and the query.
    /// </summary>
    /// <param name="identifier">The API identifier, used for error messages.</param>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <returns>The absolute request URI.</returns>
    public Uri ResolveUri(string identifier, string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
            throw ApiConfigurationValidationException.InvalidValue(identifier, nameof(Configuration.ApiConfig.BaseUrl), $"'{baseUrl}' is not an absolute URL");

        string resolvedPath = SubstituteRouteValues(identifier, Path);

        // A leading slash on the relative part would discard any base path, which is never what a
        // configured base URL like "http://host/api-root/" is meant to do.
        string relative = resolvedPath.TrimStart('/');
        string separator = baseUri.AbsoluteUri.EndsWith('/') ? string.Empty : "/";
        Uri absolute = new(baseUri.AbsoluteUri + separator + relative, UriKind.Absolute);

        if (_query.Count == 0)
            return absolute;

        StringBuilder builder = new(absolute.AbsoluteUri);
        builder.Append(absolute.Query.Length > 0 ? '&' : '?');
        builder.Append(string.Join("&", _query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")));

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    /// <summary>
    /// Creates the outgoing message for this request, without authentication applied.
    /// </summary>
    /// <param name="requestUri">The absolute request URI.</param>
    /// <returns>The message to send.</returns>
    public HttpRequestMessage CreateMessage(Uri requestUri)
    {
        HttpRequestMessage message = new(Method, requestUri);

        if (BodyBytes is not null)
            message.Content = new ByteArrayContent(BodyBytes);
        else if (BodyText is not null)
            message.Content = new StringContent(BodyText, Encoding.UTF8);

        if (message.Content is not null && !string.IsNullOrWhiteSpace(ContentType))
        {
            message.Content.Headers.Remove("Content-Type");
            message.Content.Headers.TryAddWithoutValidation("Content-Type", ContentType);
        }

        foreach ((string key, string[] values) in _headers)
        {
            if (!message.Headers.TryAddWithoutValidation(key, values))
                message.Content?.Headers.TryAddWithoutValidation(key, values);
        }

        return message;
    }

    private string SubstituteRouteValues(string identifier, string path)
    {
        // The unresolved-token check runs even when no route values were supplied: a forgotten
        // WithRouteValue must fail loudly instead of sending a literal '{id}' to the server.
        string result = path;
        foreach ((string name, string value) in _routeValues)
            result = result.Replace($"{{{name}}}", Uri.EscapeDataString(value), StringComparison.Ordinal);

        int unresolved = result.IndexOf('{', StringComparison.Ordinal);
        if (unresolved >= 0)
        {
            throw ApiConfigurationValidationException.InvalidValue(
                identifier,
                "Path",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "the path '{0}' still contains an unsubstituted route token at position {1}. Supply it with WithRouteValue(...)",
                    result,
                    unresolved));
        }

        return result;
    }
}
