using System;
using System.Net;
using TestFramework.Core.Steps;

namespace TestFramework.Web.Trigger.IsLive;

/// <summary>
/// Result of an API liveness probe.
/// </summary>
/// <remarks>
/// A result only exists when the probe succeeded: a failing probe throws
/// <see cref="Exceptions.ApiLivenessProbeException"/> or
/// <see cref="Exceptions.ApiRequestFailedException"/> instead of returning. See
/// <see cref="Success"/> for why the field is here at all.
/// </remarks>
public sealed record ApiIsLiveResult : StepResultContext
{
    internal ApiIsLiveResult(string apiIdentifier, ApiAlivenessLevel alivenessLevel, Uri probeUri, bool success, HttpStatusCode? statusCode, TimeSpan elapsed)
    {
        this.ApiIdentifier = apiIdentifier;
        this.AlivenessLevel = alivenessLevel;
        this.ProbeUri = probeUri;
        this.Success = success;
        this.StatusCode = statusCode;
        this.Elapsed = elapsed;
    }

    /// <summary>
    /// The API identifier that was probed.
    /// </summary>
    public string ApiIdentifier { get; }

    /// <summary>
    /// The requested probe depth.
    /// </summary>
    public ApiAlivenessLevel AlivenessLevel { get; }

    /// <summary>
    /// The URI that was probed.
    /// </summary>
    public Uri ProbeUri { get; }

    /// <summary>
    /// Always <see langword="true"/>. A failed probe throws rather than returning a result, so there is no value of this record in which it is false. It exists so that an asserted probe reads like any other step result — <c>run.ApiProbe("live").Select(p =&gt; p.Success).Should().Be(true)</c> — instead of forcing a different assertion shape on the one step that cannot fail quietly.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// The status code returned by the probe, when a response arrived.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// Wall-clock duration of the probe.
    /// </summary>
    public TimeSpan Elapsed { get; }
}
