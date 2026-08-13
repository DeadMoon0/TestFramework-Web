namespace TestFramework.Web.Sql;

/// <summary>
/// Supplies SQL credentials at run time, overriding whatever the configuration declares.
/// </summary>
/// <remarks>
/// Register an implementation when credentials come from somewhere the configuration file cannot
/// reach, for example a secret store, or when one test settings file has to serve both a developer
/// machine using integrated security and a container using a SQL login.
/// </remarks>
public interface ISqlCredentialProvider
{
    /// <summary>
    /// Returns the credentials for an identifier, or <see langword="null"/> to keep the configured ones.
    /// </summary>
    /// <param name="identifier">The SQL identifier being resolved.</param>
    SqlCredentials? GetCredentials(string identifier);
}

/// <summary>
/// Credentials used to authenticate against SQL Server.
/// </summary>
/// <param name="IntegratedSecurity">Authenticates as the current process identity.</param>
/// <param name="UserName">SQL login name, when not using integrated security.</param>
/// <param name="Password">SQL login password. Never logged.</param>
public sealed record SqlCredentials(bool IntegratedSecurity, string? UserName = null, string? Password = null)
{
    /// <summary>
    /// Credentials that authenticate as the current process identity.
    /// </summary>
    public static SqlCredentials Integrated { get; } = new(true);

    /// <summary>
    /// Creates credentials for a SQL login.
    /// </summary>
    /// <param name="userName">The login name.</param>
    /// <param name="password">The password.</param>
    public static SqlCredentials Login(string userName, string password) => new(false, userName, password);
}
