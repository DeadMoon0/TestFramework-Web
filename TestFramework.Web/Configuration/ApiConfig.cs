using System;
using TestFramework.Web.Auth;

namespace TestFramework.Web.Configuration;

/// <summary>
/// Configuration required to call a REST API.
/// </summary>
/// <remarks>
/// The identifier maps to a named entry under the <c>Api</c> section. Every value has a usable default,
/// and an entry may leave the address open for something that supplies it later.
/// </remarks>
public record ApiConfig
{
    /// <summary>
    /// Where it answers, when that is known yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Absent is a normal entry rather than an incomplete one. A resource a container is about to start has
    /// no address until it does, and the rest of the entry - a health path, an auth mode, a poll interval -
    /// is perfectly statable in advance. Requiring one here forced a placeholder nobody dials.
    /// </para>
    /// <para>
    /// A record rebuilt from a run still always has one: the shape refuses to rebuild without an address, so
    /// anything holding one of these because a step asked for it can rely on this. Only an entry read
    /// straight out of configuration may leave it open.
    /// </para>
    /// </remarks>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// The address, for something about to use it.
    /// </summary>
    /// <remarks>
    /// The invariant stated once rather than asserted at each call site. A record rebuilt from a run always
    /// has an address - the shape refuses to rebuild without one - so this cannot fail for a config a step
    /// was handed; it can only fail for an entry somebody read straight out of configuration and then tried
    /// to connect with, which is the case that deserves a sentence rather than a null reference.
    /// </remarks>
    /// <exception cref="TestFramework.Core.Exceptions.FrameworkConfigurationException">The entry states no address.</exception>
    public string BaseUrlRequired
        => this.BaseUrl is { Length: > 0 } baseUrl
            ? baseUrl
            : throw new TestFramework.Core.Exceptions.FrameworkConfigurationException(
                "This entry states no address, so there is nothing to call.")
            {
                RecoverySteps = ["Set its BaseUrl, or include the definition that provisions it so it publishes one when it starts."],
            };


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

    /// <summary>
    /// Keeps a cookie jar across the requests of this identifier. Off by default.
    /// </summary>
    /// <remarks>
    /// Clients are pooled per identifier, so the jar is shared by every run and every test that
    /// talks to this identifier in the same process. With it on, a <c>Set-Cookie</c> from one run —
    /// a session id, an authentication cookie, an anti-forgery token, a sticky-session affinity
    /// hint — is replayed on the next run's first request. That is exactly the hidden state a test
    /// run must not carry, which is why it is off unless the API genuinely needs a cookie session.
    /// Turn it on only when the API under test cannot be driven without one, and expect concurrent
    /// runs against the same identifier to share the jar.
    /// </remarks>
    public bool UseCookies { get; init; }
}
