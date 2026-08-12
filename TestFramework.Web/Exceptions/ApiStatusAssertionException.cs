using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using TestFramework.Core.Exceptions;
using TestFramework.Web.Http;

namespace TestFramework.Web.Exceptions;

/// <summary>
/// Thrown when an asserted response status code does not match the actual response.
/// </summary>
/// <remarks>
/// The message carries everything needed to diagnose the call without re-running it: the request,
/// the actual status, how long it took, the correlation headers and a bounded body excerpt.
/// </remarks>
public sealed class ApiStatusAssertionException : TimelineFrameworkException
{
    /// <summary>
    /// Header names surfaced in the failure message because they usually identify the call in server logs.
    /// </summary>
    private static readonly string[] DiagnosticHeaders =
    [
        "x-correlation-id",
        "x-request-id",
        "traceparent",
        "request-id",
    ];

    /// <summary>
    /// Creates an exception describing a status-code mismatch.
    /// </summary>
    /// <param name="response">The response that failed the assertion.</param>
    /// <param name="expectation">A description of what was expected, for example "200 OK".</param>
    /// <returns>The exception describing the mismatch.</returns>
    public static ApiStatusAssertionException Mismatch(HttpResponseContext response, string expectation)
    {
        ArgumentNullException.ThrowIfNull(response);

        List<string> details =
        [
            $"Expected {expectation} but the API answered {(int)response.StatusCode} {response.StatusCode}.",
            $"Request: {response.RequestMethod} {response.RequestUri}",
            $"Elapsed: {response.Elapsed:g}",
        ];

        foreach (string headerName in DiagnosticHeaders)
        {
            if (response.Headers.TryGetValue(headerName, out string[]? values) && values.Length > 0)
                details.Add($"{headerName}: {string.Join(", ", values)}");
        }

        string? excerpt = response.BodyExcerpt();
        if (!string.IsNullOrWhiteSpace(excerpt))
            details.Add($"Body: {excerpt}");

        List<string> recovery =
        [
            "Look the request up in the API log using the correlation header above.",
        ];

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            recovery.Add($"Check the Auth mode configured for API '{response.ApiIdentifier}'; Negotiate is required for Windows integrated authentication.");

        if (response.StatusCode == HttpStatusCode.NotFound)
            recovery.Add("Verify the request path; it is composed from BaseUrl plus the path passed to the trigger.");

        if ((int)response.StatusCode >= 500)
            recovery.Add("A 5xx is a server-side fault: the API log, not the test, holds the cause.");

        return new ApiStatusAssertionException(string.Join(System.Environment.NewLine, details), recovery);
    }

    private ApiStatusAssertionException(string friendlyMessage, IReadOnlyList<string> recoverySteps)
        : base(friendlyMessage, recoverySteps)
    {
    }
}
