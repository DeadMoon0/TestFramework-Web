using System;

namespace TestFramework.Web.Stub;

/// <summary>
/// Configuration required to reach a stub server and its administration surface.
/// </summary>
/// <remarks>
/// A stub is reached like any other web resource: by identifier. Whether it is hosted by a container
/// this run started, or was already running somewhere, is not visible to the timeline.
/// </remarks>
public record StubConfig
{
    /// <summary>
    /// Absolute address the stub answers on, for example <c>http://localhost:32770/</c>.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Path of the administration surface, relative to <see cref="BaseUrl"/>.
    /// </summary>
    /// <remarks>
    /// Verification reads the request log from here, so a stub with the administration surface
    /// disabled cannot be asserted against.
    /// </remarks>
    public string AdminPath { get; init; } = "/__admin";

    /// <summary>
    /// How often the request log is polled while waiting for a call.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Accepts server certificates that fail validation. Intended for local hosts.
    /// </summary>
    public bool AllowInvalidCertificates { get; init; }
}
