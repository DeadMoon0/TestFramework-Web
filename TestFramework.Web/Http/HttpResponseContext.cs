using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Steps;
using TestFramework.Web.Exceptions;

namespace TestFramework.Web.Http;

/// <summary>
/// Serializable HTTP response context returned by API triggers.
/// </summary>
/// <param name="ApiIdentifier">The API identifier that produced the response.</param>
/// <param name="RequestMethod">The HTTP method that was sent.</param>
/// <param name="RequestUri">The absolute URI that was called.</param>
/// <param name="StatusCode">The response status code.</param>
/// <param name="ReasonPhrase">The response reason phrase, when the server sent one.</param>
/// <param name="ContentType">The response content type, when present.</param>
/// <param name="Body">The response body as text, when the response had content.</param>
/// <param name="Headers">Response and content headers, merged and case-insensitive.</param>
/// <param name="Elapsed">Wall-clock duration of the call.</param>
/// <remarks>
/// This type is a plain data record on purpose: step results travel to the debugging UI, so a live
/// <see cref="HttpResponseMessage"/> must never leave the trigger.
/// </remarks>
public sealed record HttpResponseContext(
    string ApiIdentifier,
    string RequestMethod,
    Uri RequestUri,
    HttpStatusCode StatusCode,
    string? ReasonPhrase,
    string? ContentType,
    string? Body,
    IReadOnlyDictionary<string, string[]> Headers,
    TimeSpan Elapsed) : StepResultContext
{
    private const int BodyExcerptLimit = 2048;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            return JsonSerializer.Deserialize<T>(Body, JsonOptions)
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
