using System.Collections.Generic;
using TestFramework.Core.Variables;
using TestFramework.Web.Auth;
using TestFramework.Web.Builder.Stages;

namespace TestFramework.Web.Builder.Actions;

/// <summary>
/// Adds route values to an API request.
/// </summary>
public interface IWithRouteValueAction
{
    /// <summary>
    /// Substitutes a <c>{name}</c> token in the path with a variable value.
    /// </summary>
    /// <param name="name">The token name without braces.</param>
    /// <param name="value">The value to substitute, URL-escaped automatically.</param>
    IApiPayloadStage WithRouteValue(string name, VariableReference<string> value);
}

/// <summary>
/// Adds query string values to an API request.
/// </summary>
public interface IWithQueryAction
{
    /// <summary>
    /// Appends a query string value. Escaping is handled automatically.
    /// </summary>
    /// <param name="name">The query parameter name.</param>
    /// <param name="value">The query parameter value. A null-resolving variable omits the parameter.</param>
    IApiPayloadStage WithQuery(string name, VariableReference<string> value);
}

/// <summary>
/// Adds headers to an API request.
/// </summary>
public interface IWithHeaderAction
{
    /// <summary>
    /// Adds a single-valued header.
    /// </summary>
    /// <param name="key">The header name.</param>
    /// <param name="value">The header value.</param>
    IApiPayloadStage WithHeader(VariableReference<string> key, VariableReference<string> value);

    /// <summary>
    /// Adds a multi-valued header.
    /// </summary>
    /// <param name="key">The header name.</param>
    /// <param name="values">The header values.</param>
    IApiPayloadStage WithHeader(VariableReference<string> key, VariableReference<string[]> values);
}

/// <summary>
/// Adds a request body to an API request.
/// </summary>
public interface IWithBodyAction
{
    /// <summary>
    /// Sends a text body.
    /// </summary>
    /// <param name="text">The body text.</param>
    /// <param name="contentType">The content type to declare. Defaults to <c>text/plain; charset=utf-8</c>.</param>
    IApiPayloadStage WithBody(VariableReference<string> text, string? contentType = null);

    /// <summary>
    /// Sends a binary body.
    /// </summary>
    /// <param name="data">The body bytes.</param>
    /// <param name="contentType">The content type to declare. Defaults to <c>application/octet-stream</c>.</param>
    IApiPayloadStage WithBody(VariableReference<byte[]> data, string? contentType = null);

    /// <summary>
    /// Serializes a value as JSON and sends it as the request body.
    /// </summary>
    /// <typeparam name="TValue">The value type to serialize.</typeparam>
    /// <param name="value">The value variable.</param>
    IApiPayloadStage WithJsonBody<TValue>(VariableReference<TValue> value);
}

/// <summary>
/// Overrides the configured authentication for a single API request.
/// </summary>
public interface IWithAuthAction
{
    /// <summary>
    /// Replaces the configured authentication mode for this request only.
    /// </summary>
    /// <param name="provider">The authentication provider to apply.</param>
    IApiPayloadStage WithAuth(IApiAuthenticationProvider provider);

    /// <summary>
    /// Sends a bearer token for this request only.
    /// </summary>
    /// <param name="token">The token value. Never logged.</param>
    IApiPayloadStage WithBearerToken(VariableReference<string> token);
}

/// <summary>
/// Adds several headers at once to an API request.
/// </summary>
public interface IWithHeadersAction
{
    /// <summary>
    /// Adds every entry of a header dictionary.
    /// </summary>
    /// <param name="headers">The headers to add.</param>
    IApiPayloadStage WithHeaders(VariableReference<Dictionary<string, string>> headers);
}
