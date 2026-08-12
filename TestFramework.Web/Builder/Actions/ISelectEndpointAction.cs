using System.Net.Http;
using TestFramework.Core.Variables;
using TestFramework.Web.Builder.Stages;

namespace TestFramework.Web.Builder.Actions;

/// <summary>
/// Selects the method and path of an API request.
/// </summary>
/// <remarks>
/// The path is the contract. It is never derived from the application's types, so a route change
/// on the server makes the test fail rather than silently follow along.
/// </remarks>
public interface ISelectEndpointAction
{
    /// <summary>
    /// Selects a <c>GET</c> request for a relative path.
    /// </summary>
    /// <param name="path">The path relative to the configured base URL, for example <c>api/items/{id}</c>.</param>
    IApiPayloadStage Get(string path);

    /// <summary>
    /// Selects a <c>GET</c> request for a variable-backed relative path.
    /// </summary>
    /// <param name="path">The path variable.</param>
    IApiPayloadStage Get(VariableReference<string> path);

    /// <summary>
    /// Selects a <c>POST</c> request for a relative path.
    /// </summary>
    /// <param name="path">The path relative to the configured base URL.</param>
    IApiPayloadStage Post(string path);

    /// <summary>
    /// Selects a <c>POST</c> request for a variable-backed relative path.
    /// </summary>
    /// <param name="path">The path variable.</param>
    IApiPayloadStage Post(VariableReference<string> path);

    /// <summary>
    /// Selects a <c>PUT</c> request for a relative path.
    /// </summary>
    /// <param name="path">The path relative to the configured base URL.</param>
    IApiPayloadStage Put(string path);

    /// <summary>
    /// Selects a <c>PUT</c> request for a variable-backed relative path.
    /// </summary>
    /// <param name="path">The path variable.</param>
    IApiPayloadStage Put(VariableReference<string> path);

    /// <summary>
    /// Selects a <c>PATCH</c> request for a relative path.
    /// </summary>
    /// <param name="path">The path relative to the configured base URL.</param>
    IApiPayloadStage Patch(string path);

    /// <summary>
    /// Selects a <c>DELETE</c> request for a relative path.
    /// </summary>
    /// <param name="path">The path relative to the configured base URL.</param>
    IApiPayloadStage Delete(string path);

    /// <summary>
    /// Selects a request with an explicit method.
    /// </summary>
    /// <param name="method">The HTTP method to use.</param>
    /// <param name="path">The path variable.</param>
    IApiPayloadStage Method(HttpMethod method, VariableReference<string> path);
}
