using TestFramework.Web.Http;
using System.Net;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace TestFramework.Web.Stub.Mappings;

/// <summary>
/// What a stub answers when a request matches.
/// </summary>
/// <remarks>
/// Made with <see cref="Json"/>, <see cref="Text(HttpStatusCode, string)"/> or <see cref="Status"/>, and adjusted
/// with <c>with</c>: <c>StubResponse.Json(HttpStatusCode.Created, body) with { UseTemplating = true }</c>. The body
/// is set by the factory and nowhere else, so a response cannot carry a literal body and a JSON body at once; every
/// other knob is an init property, so a new one - a fault, a chunked body - is a new property rather than one more
/// optional parameter on every respond method.
/// </remarks>
public sealed record StubResponse
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private StubResponse(int statusCode, IReadOnlyDictionary<string, string> headers, string? body, JToken? bodyAsJson)
    {
        this.StatusCode = statusCode;
        this.Headers = headers;
        this.Body = body;
        this.BodyAsJson = bodyAsJson;
    }

    /// <summary>
    /// The status code returned.
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// Response headers.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>
    /// A literal body, when the response is not JSON.
    /// </summary>
    public string? Body { get; }

    /// <summary>
    /// A JSON body.
    /// </summary>
    public JToken? BodyAsJson { get; }

    /// <summary>
    /// How long to wait before answering, or <see langword="null"/> to answer at once.
    /// </summary>
    public TimeSpan? Delay { get; init; }

    /// <summary>
    /// Whether the body is treated as a Handlebars template, so it can quote the request back, for example
    /// <c>{{request.body.amount}}</c>.
    /// </summary>
    public bool UseTemplating { get; init; }

    /// <summary>
    /// Answers with a JSON body.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="body">The body, serialized to JSON the way a request body is.</param>
    /// <returns>The response.</returns>
    public static StubResponse Json(HttpStatusCode statusCode, object body)
    {
        ArgumentNullException.ThrowIfNull(body);

        // Through the wire serializer, so a response body a test hands over is named the same way a
        // request body is.
        JToken node = JToken.FromObject(body, WebJson.WireSerializer)
            ?? throw new ArgumentException("The response body serialized to nothing.", nameof(body));

        return new StubResponse(
            (int)statusCode,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "application/json" },
            null,
            node);
    }

    /// <summary>
    /// Answers with a literal <c>text/plain</c> body.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="body">The body, sent as it is.</param>
    /// <returns>The response.</returns>
    public static StubResponse Text(HttpStatusCode statusCode, string body) => Text(statusCode, body, "text/plain");

    /// <summary>
    /// Answers with a literal body of a stated content type.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <param name="body">The body, sent as it is.</param>
    /// <param name="contentType">The content type.</param>
    /// <returns>The response.</returns>
    public static StubResponse Text(HttpStatusCode statusCode, string body, string contentType)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        return new StubResponse(
            (int)statusCode,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = contentType },
            body,
            null);
    }

    /// <summary>
    /// Answers with a status code and no body.
    /// </summary>
    /// <param name="statusCode">The status code.</param>
    /// <returns>The response.</returns>
    public static StubResponse Status(HttpStatusCode statusCode) => new((int)statusCode, NoHeaders, null, null);
}

/// <summary>
/// One request the stub recognises, and the answer it gives.
/// </summary>
/// <remarks>
/// This model is deliberately declarative and free of delegates: the same declaration has to be
/// runnable by a stub server in another process, which cannot call back into the test.
/// </remarks>
public sealed record StubMapping
{
    internal StubMapping(string method, string path, IReadOnlyDictionary<string, string> headers, IReadOnlyDictionary<string, string> query, string? bodyContains, int priority, StubResponse response)
    {
        this.Method = method;
        this.Path = path;
        this.Headers = headers;
        this.Query = query;
        this.BodyContains = bodyContains;
        this.Priority = priority;
        this.Response = response;
    }

    /// <summary>
    /// The HTTP method.
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// The path, where <c>*</c> matches any run of characters.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Headers that must be present, and the value each must have. An empty value means "any".
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    /// Query parameters that must be present, and the value each must have.
    /// </summary>
    public IReadOnlyDictionary<string, string> Query { get; }

    /// <summary>
    /// Text the request body must contain.
    /// </summary>
    public string? BodyContains { get; }

    /// <summary>
    /// Lower wins when more than one mapping matches.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// The answer.
    /// </summary>
    public StubResponse Response { get; }

    /// <summary>
    /// Returns a readable description of the mapping.
    /// </summary>
    public override string ToString() => $"{Method} {Path} -> {Response.StatusCode}";
}
