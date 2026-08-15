using System;

namespace TestFramework.Web.Trigger;

/// <summary>
/// Run-level tuning for API triggers.
/// </summary>
/// <remarks>
/// Register an instance in the run service collection to override the defaults. These knobs are
/// deliberately narrower than timeline retries: they absorb host startup quirks, not application
/// behaviour, which callers should model with explicit step retries instead.
/// </remarks>
public sealed record ApiTriggerConfig
{
    /// <summary>
    /// How long <c>WebExt.Api.IsLive(...)</c> keeps waiting on a 404 or 503 from a local host while
    /// its route table warms up.
    /// </summary>
    /// <remarks>
    /// Only the liveness probe waits. An ordinary call is sent exactly once, so a deliberate 404
    /// assertion returns immediately. Only loopback and <c>host.docker.internal</c> authorities are
    /// treated as warming up; anywhere else a 404 is a real answer and fails the probe at once.
    /// Set to <see cref="TimeSpan.Zero"/> to make the probe single-shot.
    /// </remarks>
    public TimeSpan LocalWarmupRetryDuration { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Delay between liveness probe attempts while waiting out a local warmup.
    /// </summary>
    public TimeSpan LocalWarmupRetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Logs the resolved request URI and the response status for every call.
    /// </summary>
    public bool LogRequests { get; init; } = true;

    /// <summary>
    /// Logs the outgoing request headers, with sensitive values redacted.
    /// </summary>
    /// <remarks>
    /// Off by default because it is noisy. Turn it on when diagnosing authentication or routing
    /// problems; which header values stay hidden is controlled by <c>Web:SensitiveHeaders</c>.
    /// </remarks>
    public bool LogRequestHeaders { get; init; }
}
