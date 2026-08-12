using System;
using System.Net;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Builder.TimelineBuilder;
using TestFramework.Core.Variables;
using TestFramework.Web.Exceptions;
using TestFramework.Web.Http;
using TestFramework.Web.Trigger.IsLive;

namespace TestFramework.Web;

/// <summary>
/// Typed result helpers for web timeline steps.
/// </summary>
public static class WebTimelineResultExtensions
{
    /// <summary>
    /// Returns the response captured by an API step.
    /// </summary>
    /// <param name="handle">The executed step.</param>
    /// <returns>The captured response context.</returns>
    /// <exception cref="InvalidOperationException">The step did not produce an API response.</exception>
    public static HttpResponseContext Response(this StepHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        return handle.LastResult.Result as HttpResponseContext
            ?? throw new InvalidOperationException(
                $"Step '{handle.Label ?? handle.Step.Name}' did not produce an {nameof(HttpResponseContext)}. "
                + $"Its last result was '{handle.LastResult.Result?.GetType().Name ?? "null"}'.");
    }

    /// <summary>
    /// Asserts the response status code and returns the response for further assertions.
    /// </summary>
    /// <param name="handle">The executed step.</param>
    /// <param name="expected">The expected status code.</param>
    /// <returns>The captured response context.</returns>
    /// <exception cref="ApiStatusAssertionException">The status code does not match.</exception>
    public static HttpResponseContext ExpectStatus(this StepHandle handle, HttpStatusCode expected)
    {
        HttpResponseContext response = handle.Response();
        return response.StatusCode == expected
            ? response
            : throw ApiStatusAssertionException.Mismatch(response, $"{(int)expected} {expected}");
    }

    /// <summary>
    /// Asserts the response status code is in the 2xx range and returns the response.
    /// </summary>
    /// <param name="handle">The executed step.</param>
    /// <returns>The captured response context.</returns>
    /// <exception cref="ApiStatusAssertionException">The status code is not successful.</exception>
    public static HttpResponseContext ExpectSuccess(this StepHandle handle)
    {
        HttpResponseContext response = handle.Response();
        return response.IsSuccess
            ? response
            : throw ApiStatusAssertionException.Mismatch(response, "a 2xx status code");
    }

    /// <summary>
    /// Asserts a successful response and deserializes its body as JSON.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="handle">The executed step.</param>
    /// <returns>The deserialized body.</returns>
    public static T ExpectJson<T>(this StepHandle handle) => handle.ExpectSuccess().Json<T>();

    /// <summary>
    /// Returns the liveness probe result captured by an is-live step.
    /// </summary>
    /// <param name="handle">The executed step.</param>
    /// <returns>The captured probe result.</returns>
    /// <exception cref="InvalidOperationException">The step did not produce a probe result.</exception>
    public static ApiIsLiveResult ProbeResult(this StepHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        return handle.LastResult.Result as ApiIsLiveResult
            ?? throw new InvalidOperationException(
                $"Step '{handle.Label ?? handle.Step.Name}' did not produce an {nameof(ApiIsLiveResult)}. "
                + $"Its last result was '{handle.LastResult.Result?.GetType().Name ?? "null"}'.");
    }

    /// <summary>
    /// Binds the full API response context into a variable.
    /// </summary>
    /// <param name="builder">The timeline builder modifier for the API step.</param>
    /// <param name="identifier">The variable identifier to bind.</param>
    public static ITimelineBuilderModifier<HttpResponseContext> GetResponse(this ITimelineBuilderModifier<HttpResponseContext> builder, VariableIdentifier identifier)
        => builder.BindResultProperty(x => x, identifier);

    /// <summary>
    /// Binds the API response status code into a variable.
    /// </summary>
    /// <param name="builder">The timeline builder modifier for the API step.</param>
    /// <param name="identifier">The variable identifier to bind.</param>
    public static ITimelineBuilderModifier<HttpResponseContext> GetStatusCode(this ITimelineBuilderModifier<HttpResponseContext> builder, VariableIdentifier identifier)
        => builder.BindResultProperty(x => x.StatusCode, identifier);

    /// <summary>
    /// Binds the API response body into a variable.
    /// </summary>
    /// <param name="builder">The timeline builder modifier for the API step.</param>
    /// <param name="identifier">The variable identifier to bind.</param>
    public static ITimelineBuilderModifier<HttpResponseContext> GetBody(this ITimelineBuilderModifier<HttpResponseContext> builder, VariableIdentifier identifier)
        => builder.BindResultProperty(x => x.Body, identifier);

    /// <summary>
    /// Binds the API response headers into a variable.
    /// </summary>
    /// <param name="builder">The timeline builder modifier for the API step.</param>
    /// <param name="identifier">The variable identifier to bind.</param>
    public static ITimelineBuilderModifier<HttpResponseContext> GetHeaders(this ITimelineBuilderModifier<HttpResponseContext> builder, VariableIdentifier identifier)
        => builder.BindResultProperty(x => x.Headers, identifier);

    /// <summary>
    /// Binds the liveness probe result into a variable.
    /// </summary>
    /// <param name="builder">The timeline builder modifier for the is-live step.</param>
    /// <param name="identifier">The variable identifier to bind.</param>
    public static ITimelineBuilderModifier<ApiIsLiveResult> GetProbeResult(this ITimelineBuilderModifier<ApiIsLiveResult> builder, VariableIdentifier identifier)
        => builder.BindResultProperty(x => x, identifier);
}
