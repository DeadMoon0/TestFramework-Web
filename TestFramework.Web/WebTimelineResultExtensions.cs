using System;
using System.Net;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Timelines.Builder.TimelineBuilder;
using TestFramework.Core.Variables;
using TestFramework.Web.Http;
using TestFramework.Web.Trigger.IsLive;

namespace TestFramework.Web;

/// <summary>
/// Typed result helpers for web timeline steps.
/// </summary>
/// <remarks>
/// The assertion entry points return the framework's own <see cref="ValueHandle{T}"/>, so web
/// assertions behave exactly like Core assertions: they are signalled to the debugging UI, they
/// participate in <c>run.AssertionScope()</c>, and they fail with the framework exception types.
/// </remarks>
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
    /// Starts an assertion chain for the whole response of an API step.
    /// </summary>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    public static ValueHandle<HttpResponseContext> ApiResponse(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Assert(run.Step(label).Response(), $"'{label}' response");
    }

    /// <summary>
    /// Starts an assertion chain for the status code of an API step.
    /// </summary>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    public static ValueHandle<HttpStatusCode> ApiStatus(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);
        HttpResponseContext response = run.Step(label).Response();

        // The expression carries the request and the timing, so an assertion failure identifies the
        // call without the reader having to re-run anything.
        return run.Assert(response.StatusCode, $"'{label}' status of {response.Summary()}");
    }

    /// <summary>
    /// Starts an assertion chain for the response body of an API step.
    /// </summary>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    public static ValueHandle<string?> ApiBody(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);
        HttpResponseContext response = run.Step(label).Response();
        return run.Assert(response.Body, $"'{label}' body of {response.Summary()}");
    }

    /// <summary>
    /// Starts an assertion chain for a single response header of an API step.
    /// </summary>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    /// <param name="headerName">The header name, matched case-insensitively.</param>
    public static ValueHandle<string?> ApiHeader(this TimelineRun run, string label, string headerName)
    {
        ArgumentNullException.ThrowIfNull(run);
        HttpResponseContext response = run.Step(label).Response();
        return run.Assert(response.Header(headerName), $"'{label}' header '{headerName}'");
    }

    /// <summary>
    /// Deserializes the response body of an API step and starts an assertion chain for it.
    /// </summary>
    /// <typeparam name="T">The type to deserialize into.</typeparam>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    public static ValueHandle<T> ApiJson<T>(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);
        HttpResponseContext response = run.Step(label).Response();
        return run.Assert(response.Json<T>(), $"'{label}' body as {typeof(T).Name}");
    }

    /// <summary>
    /// Starts an assertion chain for the probe result of a liveness step.
    /// </summary>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    public static ValueHandle<ApiIsLiveResult> ApiProbe(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Assert(run.Step(label).ProbeResult(), $"'{label}' probe");
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
