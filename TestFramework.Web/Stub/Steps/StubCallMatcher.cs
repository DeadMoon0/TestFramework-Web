using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Web.Stub.Admin;

namespace TestFramework.Web.Stub.Steps;

/// <summary>
/// Matching rules shared by the step that reads a stub's log and the event that waits on it.
/// </summary>
/// <remarks>
/// Both look at the same request log and must agree on what counts, or a wait and the assertion
/// after it would disagree about the very same call.
/// </remarks>
internal static class StubCallMatcher
{
    /// <summary>
    /// Returns whether a call is inside the observation window a reset opened.
    /// </summary>
    /// <param name="call">The logged call.</param>
    /// <param name="watermark">The newest timestamp that predates this run, or <see langword="null"/> for the whole log.</param>
    /// <remarks>
    /// A call with no timestamp counts as in scope. The stub did not say when it arrived, so it
    /// cannot be placed relative to the watermark, and dropping it would hide evidence rather than
    /// scope it.
    /// </remarks>
    public static bool IsInScope(StubCall call, DateTimeOffset? watermark)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (watermark is not { } cutoff || call.ReceivedAt is not { } receivedAt)
            return true;

        return receivedAt > cutoff;
    }

    /// <summary>
    /// Returns whether every required header is present on the call with the expected value.
    /// </summary>
    /// <param name="call">The logged call.</param>
    /// <param name="required">The header filters, or an empty set for no header requirement.</param>
    public static bool HasHeaders(StubCall call, IReadOnlyDictionary<string, string> required)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(required);

        foreach ((string name, string expected) in required)
        {
            if (!call.Headers.TryGetValue(name, out string? actual) || actual is null)
                return false;

            if (!ValueMatches(actual, expected))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the newest timestamp in a log, or <see langword="null"/> when none carries one.
    /// </summary>
    /// <param name="calls">The calls to inspect.</param>
    public static DateTimeOffset? NewestTimestamp(IEnumerable<StubCall> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);

        DateTimeOffset? newest = null;
        foreach (StubCall call in calls)
        {
            if (call.ReceivedAt is { } receivedAt && (newest is null || receivedAt > newest))
                newest = receivedAt;
        }

        return newest;
    }

    // A multi-valued header arrives joined with ", ", so compare against the parts as well as the
    // whole: a caller filtering on one correlation id should not have to know it was sent twice.
    private static bool ValueMatches(string actual, string expected)
        => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
        || actual.Split(',').Any(part => string.Equals(part.Trim(), expected, StringComparison.OrdinalIgnoreCase));
}
