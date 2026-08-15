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
/// <paramref name="Success"/> for why the field is here at all.
/// </remarks>
/// <param name="ApiIdentifier">The API identifier that was probed.</param>
/// <param name="AlivenessLevel">The requested probe depth.</param>
/// <param name="ProbeUri">The URI that was probed.</param>
/// <param name="Success">
/// Always <see langword="true"/>. A failed probe throws rather than returning a result, so there is
/// no value of this record in which it is false. It exists so that an asserted probe reads like any
/// other step result — <c>run.ApiProbe("live").Select(p =&gt; p.Success).Should().Be(true)</c> — instead
/// of forcing a different assertion shape on the one step that cannot fail quietly.
/// </param>
/// <param name="StatusCode">The status code returned by the probe, when a response arrived.</param>
/// <param name="Elapsed">Wall-clock duration of the probe.</param>
public sealed record ApiIsLiveResult(
    string ApiIdentifier,
    ApiAlivenessLevel AlivenessLevel,
    Uri ProbeUri,
    bool Success,
    HttpStatusCode? StatusCode,
    TimeSpan Elapsed) : StepResultContext;
