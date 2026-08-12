using System;
using System.Collections.Generic;
using System.Net;
using TestFramework.Core.Exceptions;
using TestFramework.Web.Trigger.IsLive;

namespace TestFramework.Web.Exceptions;

/// <summary>
/// Thrown when an API liveness probe answers but does not meet the requested level.
/// </summary>
public sealed class ApiLivenessProbeException : TimelineFrameworkException
{
    /// <summary>
    /// Creates an exception describing a probe that answered with an unacceptable status code.
    /// </summary>
    /// <param name="identifier">The API identifier that was probed.</param>
    /// <param name="level">The requested probe depth.</param>
    /// <param name="probeUri">The URI that was probed.</param>
    /// <param name="statusCode">The status code returned by the probe.</param>
    /// <param name="additionalRecovery">Recovery hints specific to the status code.</param>
    /// <returns>The exception describing the failed probe.</returns>
    public static ApiLivenessProbeException Failed(
        string identifier,
        ApiAlivenessLevel level,
        Uri probeUri,
        HttpStatusCode statusCode,
        IReadOnlyList<string> additionalRecovery)
    {
        ArgumentNullException.ThrowIfNull(probeUri);
        ArgumentNullException.ThrowIfNull(additionalRecovery);

        List<string> recovery = [.. additionalRecovery];
        recovery.Add($"Use .WithTimeOut(...) and .WithRetry(...) when the API needs time to start.");

        return new ApiLivenessProbeException(
            $"API '{identifier}' answered {(int)statusCode} {statusCode} for the {level} probe of '{probeUri}'.",
            recovery);
    }

    private ApiLivenessProbeException(string friendlyMessage, IReadOnlyList<string> recoverySteps)
        : base(friendlyMessage, recoverySteps)
    {
    }
}
