using System;

namespace TestFramework.Web.Sql;

/// <summary>
/// Configuration required to reach a SQL Server database.
/// </summary>
/// <remarks>
/// Either supply a complete <see cref="ConnectionString"/>, or the structured parts and let the
/// framework compose it. The structured form exists so the same test settings can run with
/// integrated security on a developer machine and with a user name and password elsewhere, without
/// hand-editing connection strings.
/// </remarks>
public record SqlConfig
{
    /// <summary>
    /// Complete connection string. When present, the structured parts are ignored except for the credentials.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Server address, for example <c>localhost,1433</c>.
    /// </summary>
    public string? Server { get; init; }

    /// <summary>
    /// Initial catalog to open.
    /// </summary>
    public string? Database { get; init; }

    /// <summary>
    /// Authenticates with the credentials of the current process instead of a user name and password.
    /// </summary>
    public bool IntegratedSecurity { get; init; }

    /// <summary>
    /// SQL login name used when <see cref="IntegratedSecurity"/> is <see langword="false"/>.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// SQL login password. Never logged.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Accepts a server certificate that fails validation. Intended for local servers and containers.
    /// </summary>
    public bool TrustServerCertificate { get; init; }

    /// <summary>
    /// Requests an encrypted connection. Defaults to the driver default when absent.
    /// </summary>
    public bool? Encrypt { get; init; }

    /// <summary>
    /// Connection timeout applied while opening a connection.
    /// </summary>
    public TimeSpan? ConnectTimeout { get; init; }

    /// <summary>
    /// Command timeout applied to statements. When absent the step timeout is the only limit.
    /// </summary>
    public TimeSpan? CommandTimeout { get; init; }
}
