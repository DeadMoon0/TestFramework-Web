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
    /// How long a transient 404 or 503 from a local host is retried while its route table warms up.
    /// </summary>
    public TimeSpan LocalWarmupRetryDuration { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Delay between local warmup retries.
    /// </summary>
    public TimeSpan LocalWarmupRetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Logs the resolved request URI and the response status for every call.
    /// </summary>
    public bool LogRequests { get; init; } = true;
}
