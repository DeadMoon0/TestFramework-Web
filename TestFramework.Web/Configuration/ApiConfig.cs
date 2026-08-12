using System;
using TestFramework.Web.Auth;

namespace TestFramework.Web.Configuration;

/// <summary>
/// Configuration required to call a REST API.
/// </summary>
/// <remarks>
/// The identifier maps to a named entry under the <c>Api</c> section. Only
/// <see cref="BaseUrl"/> is required; every other value has a usable default.
/// </remarks>
public record ApiConfig
{
    /// <summary>
    /// Absolute API host URL, for example <c>https://my-api.example.com/</c>.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Relative path probed by health-level liveness checks. Defaults to <c>/health</c>.
    /// </summary>
    public string HealthPath { get; init; } = "/health";

    /// <summary>
    /// Authentication mode applied to every request for this identifier.
    /// </summary>
    public ApiAuthMode Auth { get; init; } = ApiAuthMode.None;

    /// <summary>
    /// Header name carrying <see cref="ApiKey"/> when <see cref="Auth"/> is <see cref="ApiAuthMode.ApiKey"/>.
    /// </summary>
    public string? ApiKeyHeaderName { get; init; }

    /// <summary>
    /// Static key sent when <see cref="Auth"/> is <see cref="ApiAuthMode.ApiKey"/>. Never logged.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Bearer token sent when <see cref="Auth"/> is <see cref="ApiAuthMode.Bearer"/>. Never logged.
    /// </summary>
    public string? BearerToken { get; init; }

    /// <summary>
    /// User name sent when <see cref="Auth"/> is <see cref="ApiAuthMode.Basic"/>.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// Password sent when <see cref="Auth"/> is <see cref="ApiAuthMode.Basic"/>. Never logged.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Per-request transport timeout. When absent the step timeout is the only limit.
    /// </summary>
    public TimeSpan? RequestTimeout { get; init; }

    /// <summary>
    /// Accepts server certificates that fail validation. Intended for local hosts with self-signed certificates.
    /// </summary>
    public bool AllowInvalidCertificates { get; init; }
}
