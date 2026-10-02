using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Steps;
using TestFramework.Web.Exceptions;

namespace TestFramework.Web.Http;

/// <summary>
/// Serializable HTTP response context returned by API triggers.
/// </summary>
/// <remarks>
/// This type is a plain data record on purpose: step results travel to the debugging UI, so a live
/// <see cref="HttpResponseMessage"/> must never leave the trigger.
/// </remarks>
public sealed record HttpResponseContext : StepResultContext
{
    internal HttpResponseContext(string apiIdentifier, string requestMethod, Uri requestUri, HttpStatusCode statusCode, string? reasonPhrase, string? contentType, string? body, IReadOnlyDictionary<string, string[]> headers, TimeSpan elapsed)
    {
        this.ApiIdentifier = apiIdentifier;
        this.RequestMethod = requestMethod;
        this.RequestUri = requestUri;
        this.StatusCode = statusCode;
        this.ReasonPhrase = reasonPhrase;
        this.ContentType = contentType;
        this.Body = body;
        this.Headers = headers;
        this.Elapsed = elapsed;
    }

    /// <summary>
    /// The API identifier that produced the response.
    /// </summary>
    public string ApiIdentifier { get; }

    /// <summary>
    /// The HTTP method that was sent.
    /// </summary>
    public string RequestMethod { get; }

    /// <summary>
    /// The absolute URI that was called.
    /// </summary>
    public Uri RequestUri { get; }

    /// <summary>
    /// The response status code.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The response reason phrase, when the server sent one.
    /// </summary>
    public string? ReasonPhrase { get; }

    /// <summary>
    /// The response content type, when present.
    /// </summary>
    public string? ContentType { get; }

    /// <summary>
    /// The response body as text, when the response had content.
    /// </summary>
    public string? Body { get; }

    /// <summary>
    /// Response and content headers, merged and case-insensitive.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Headers { get; }

    /// <summary>
    /// Wall-clock duration of the call.
    /// </summary>
    public TimeSpan Elapsed { get; }

    private const int BodyExcerptLimit = 2048;

    /// <summary>
    /// Headers surfaced in call summaries because they usually identify the call in server logs.
    /// </summary>
    private static readonly string[] CorrelationHeaderNames =
    [
        "x-correlation-id",
        "x-request-id",
        "traceparent",
        "request-id",
    ];

    /// <summary>
    /// Gets a value indicating whether the status code is in the 2xx range.
    /// </summary>
    public bool IsSuccess => (int)StatusCode is >= 200 and <= 299;

    /// <summary>
    /// Deserializes the response body as JSON.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ApiResponseFormatException">The body is absent or is not valid JSON for <typeparamref name="T"/>.</exception>
    public T Json<T>()
    {
        if (string.IsNullOrWhiteSpace(Body))
            throw ApiResponseFormatException.CannotDeserialize(typeof(T), StatusCode, ContentType, BodyExcerpt());

        try
        {
            return JsonConvert.DeserializeObject<T>(Body, WebJson.Wire)
                ?? throw ApiResponseFormatException.CannotDeserialize(typeof(T), StatusCode, ContentType, BodyExcerpt());
        }
        catch (JsonException exception)
        {
            throw ApiResponseFormatException.CannotDeserialize(typeof(T), StatusCode, ContentType, BodyExcerpt(), exception);
        }
    }

    /// <summary>
    /// Returns the first value of a response header, or <see langword="null"/> when it is absent.
    /// </summary>
    /// <param name="name">The header name, matched case-insensitively.</param>
    public string? Header(string name)
        => Headers.TryGetValue(name, out string[]? values) && values.Length > 0 ? values[0] : null;

    /// <summary>
    /// Returns a one-line description of the call: request, status, duration and correlation.
    /// </summary>
    /// <remarks>
    /// Assertion failures render values through their string form, so this is what makes an
    /// in-house assertion failure identify the exact call that produced it.
    /// </remarks>
    public string Summary()
    {
        string correlation = CorrelationHeaderNames
            .Select(name => Header(name) is { } value ? $" {name}={value}" : null)
            .FirstOrDefault(value => value is not null) ?? string.Empty;

        return $"{RequestMethod} {RequestUri} -> {(int)StatusCode} {StatusCode} in {Elapsed:g}{correlation}";
    }

    /// <summary>
    /// Returns the call summary, extended with a body excerpt when the status is not successful.
    /// </summary>
    public override string ToString()
        => IsSuccess || BodyExcerpt() is not { } excerpt
            ? Summary()
            : $"{Summary()} body={excerpt}";

    /// <summary>
    /// Returns a bounded excerpt of the body suitable for log and error output.
    /// </summary>
    public string? BodyExcerpt()
    {
        if (string.IsNullOrEmpty(Body))
            return null;

        return Body.Length <= BodyExcerptLimit
            ? Body
            : Body[..BodyExcerptLimit] + $"... (truncated, {Body.Length} characters total)";
    }

    internal static async Task<HttpResponseContext> FromHttpResponseAsync(
        string apiIdentifier,
        HttpMethod requestMethod,
        Uri requestUri,
        HttpResponseMessage response,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        string? body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<KeyValuePair<string, IEnumerable<string>>> contentHeaders = response.Content?.Headers
            ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>();

        Dictionary<string, string[]> headers = response.Headers
            .Concat(contentHeaders)
            .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.SelectMany(x => x.Value).ToArray(), StringComparer.OrdinalIgnoreCase);

        return new HttpResponseContext(
            apiIdentifier,
            requestMethod.Method,
            requestUri,
            response.StatusCode,
            response.ReasonPhrase,
            response.Content?.Headers.ContentType?.ToString(),
            body,
            new ReadOnlyDictionary<string, string[]>(headers),
            elapsed);
    }
}
