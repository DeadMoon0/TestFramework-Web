using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web.Exceptions;

/// <summary>
/// Thrown when an API request cannot be delivered or no response is received.
/// </summary>
/// <remarks>
/// A response with an unsuccessful status code is not a failure at this level; it is returned to
/// the timeline so tests can assert on it. This exception covers transport problems only.
/// </remarks>
public sealed class ApiRequestFailedException : TimelineFrameworkException
{
    /// <summary>
    /// Creates an exception describing a transport-level request failure.
    /// </summary>
    /// <param name="identifier">The API identifier that was called.</param>
    /// <param name="method">The HTTP method used.</param>
    /// <param name="requestUri">The absolute request URI.</param>
    /// <param name="elapsed">How long the attempt took before failing.</param>
    /// <param name="innerException">The underlying transport exception.</param>
    /// <returns>The exception describing the failure.</returns>
    public static ApiRequestFailedException Transport(string identifier, HttpMethod method, Uri requestUri, TimeSpan elapsed, Exception innerException)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(innerException);

        List<string> recovery =
        [
            $"Verify that '{requestUri.GetLeftPart(UriPartial.Authority)}' is reachable from this machine.",
            $"Check 'Api:{identifier}:BaseUrl' for a typo or a missing port.",
        ];

        if (innerException is TaskCanceledException or TimeoutException)
            recovery.Add($"Raise 'Api:{identifier}:RequestTimeout' or the step timeout with .WithTimeOut(...) if the API is simply slow.");

        if (requestUri.Scheme == Uri.UriSchemeHttps)
            recovery.Add($"For a self-signed certificate on a local host, set 'Api:{identifier}:AllowInvalidCertificates' to true.");

        return new ApiRequestFailedException(
            $"API '{identifier}' did not answer {method} {requestUri} after {elapsed:g}: {innerException.GetType().Name}: {innerException.Message}",
            recovery,
            null,
            innerException);
    }

    private ApiRequestFailedException(string friendlyMessage, IReadOnlyList<string> recoverySteps, IReadOnlyList<string>? availableOptions, Exception? innerException)
        : base(friendlyMessage, recoverySteps, availableOptions, innerException)
    {
    }
}
