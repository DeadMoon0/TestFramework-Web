using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace TestFramework.Web.Stub.Mappings;

/// <summary>
/// What a stub answers when a request matches.
/// </summary>
/// <param name="StatusCode">The status code returned.</param>
/// <param name="Headers">Response headers.</param>
/// <param name="Body">A literal body, when the response is not JSON.</param>
/// <param name="BodyAsJson">A JSON body.</param>
/// <param name="Delay">How long to wait before answering.</param>
/// <param name="UseTemplating">
/// Whether the body is treated as a Handlebars template, so it can quote the request back.
/// </param>
public sealed record StubResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    JsonNode? BodyAsJson,
    TimeSpan? Delay,
    bool UseTemplating);

/// <summary>
/// One request the stub recognises, and the answer it gives.
/// </summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path, where <c>*</c> matches any run of characters.</param>
/// <param name="Headers">Headers that must be present, and the value each must have. An empty value means "any".</param>
/// <param name="Query">Query parameters that must be present, and the value each must have.</param>
/// <param name="BodyContains">Text the request body must contain.</param>
/// <param name="Priority">Lower wins when more than one mapping matches.</param>
/// <param name="Response">The answer.</param>
/// <remarks>
/// This model is deliberately declarative and free of delegates: the same declaration has to be
/// runnable by a stub server in another process, which cannot call back into the test.
/// </remarks>
public sealed record StubMapping(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Query,
    string? BodyContains,
    int Priority,
    StubResponse Response)
{
    /// <summary>
    /// Returns a readable description of the mapping.
    /// </summary>
    public override string ToString() => $"{Method} {Path} -> {Response.StatusCode}";
}
