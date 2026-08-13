using System;
using System.Collections.Generic;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web.Stub.Exceptions;

/// <summary>
/// Thrown when a stub's administration surface cannot be read.
/// </summary>
/// <remarks>
/// Verification depends on that surface, so a failure here means the assertions cannot be trusted
/// rather than that the assertion failed.
/// </remarks>
public sealed class StubAdminException : TimelineFrameworkException
{
    /// <summary>
    /// Creates an exception for a stub that did not answer at all.
    /// </summary>
    /// <param name="baseAddress">The address that was contacted.</param>
    /// <param name="innerException">The transport failure.</param>
    /// <returns>The exception describing the unreachable stub.</returns>
    public static StubAdminException Unreachable(Uri? baseAddress, Exception innerException)
        => new(
            $"The stub at '{baseAddress?.ToString() ?? "(no address)"}' did not answer.",
            [
                "Check that the stub is still running; a container that exited takes its request log with it.",
                "Check the configured BaseUrl for the identifier.",
            ],
            null,
            innerException);

    /// <summary>
    /// Creates an exception for an administration surface that answered with an unexpected status.
    /// </summary>
    /// <param name="baseAddress">The address that was contacted.</param>
    /// <param name="path">The administration path that was requested.</param>
    /// <param name="statusCode">The status that came back.</param>
    /// <returns>The exception describing the unexpected answer.</returns>
    public static StubAdminException UnexpectedStatus(Uri? baseAddress, string path, int statusCode)
        => new(
            $"The stub at '{baseAddress?.ToString() ?? "(no address)"}' answered '{path}' with {statusCode}.",
            [
                "Check that the administration surface is enabled on the stub server.",
                "Check the configured AdminPath; it defaults to '/__admin'.",
            ]);

    /// <summary>
    /// Creates an exception for an administration payload that could not be read.
    /// </summary>
    /// <param name="innerException">The parse failure.</param>
    /// <returns>The exception describing the unreadable payload.</returns>
    public static StubAdminException UnreadablePayload(Exception innerException)
        => new(
            "The stub's administration surface returned something that is not JSON.",
            ["Check that the configured address points at a stub server and not at the application under test."],
            null,
            innerException);

    private StubAdminException(string friendlyMessage, IReadOnlyList<string> recoverySteps, IReadOnlyList<string>? availableOptions = null, Exception? innerException = null)
        : base(friendlyMessage, recoverySteps, availableOptions, innerException)
    {
    }
}
