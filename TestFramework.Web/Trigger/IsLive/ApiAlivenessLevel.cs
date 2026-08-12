namespace TestFramework.Web.Trigger.IsLive;

/// <summary>
/// Depth of an API liveness probe.
/// </summary>
public enum ApiAlivenessLevel
{
    /// <summary>
    /// The base URL answers at all, whatever the status code.
    /// </summary>
    /// <remarks>
    /// Use this while waiting for a host to boot: it proves the socket is open without assuming
    /// anything about routes or authorization.
    /// </remarks>
    Reachable = 0,

    /// <summary>
    /// The configured health path answers with a success status code.
    /// </summary>
    Healthy = 1,

    /// <summary>
    /// The configured health path answers with a success status code and does not reject the configured credentials.
    /// </summary>
    Authenticated = 2,
}
