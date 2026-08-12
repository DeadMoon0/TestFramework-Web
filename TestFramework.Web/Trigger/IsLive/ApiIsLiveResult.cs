using System;
using System.Net;
using TestFramework.Core.Steps;

namespace TestFramework.Web.Trigger.IsLive;

/// <summary>
/// Result of an API liveness probe.
/// </summary>
/// <param name="ApiIdentifier">The API identifier that was probed.</param>
/// <param name="AlivenessLevel">The requested probe depth.</param>
/// <param name="ProbeUri">The URI that was probed.</param>
/// <param name="Success">Whether the probe succeeded.</param>
/// <param name="StatusCode">The status code returned by the probe, when a response arrived.</param>
/// <param name="Elapsed">Wall-clock duration of the probe.</param>
public sealed record ApiIsLiveResult(
    string ApiIdentifier,
    ApiAlivenessLevel AlivenessLevel,
    Uri ProbeUri,
    bool Success,
    HttpStatusCode? StatusCode,
    TimeSpan Elapsed) : StepResultContext;
