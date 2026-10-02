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
/// <remarks>
/// Made through <see cref="Integrated"/> or <see cref="Login"/> only, so a value cannot say "integrated security"
/// and carry a password at the same time.
/// </remarks>
public sealed record SqlCredentials
{
    private SqlCredentials(bool integratedSecurity, string? userName, string? password)
    {
        this.IntegratedSecurity = integratedSecurity;
        this.UserName = userName;
        this.Password = password;
    }

    /// <summary>
    /// Whether this authenticates as the current process identity.
    /// </summary>
    public bool IntegratedSecurity { get; }

    /// <summary>
    /// The SQL login name, for a login.
    /// </summary>
    public string? UserName { get; }

    /// <summary>
    /// The SQL login password, for a login. Never logged.
    /// </summary>
    public string? Password { get; }

    /// <summary>
    /// Credentials that authenticate as the current process identity.
    /// </summary>
    public static SqlCredentials Integrated { get; } = new(true, null, null);

    /// <summary>
    /// Creates credentials for a SQL login.
    /// </summary>
    /// <param name="userName">The login name.</param>
    /// <param name="password">The password.</param>
    public static SqlCredentials Login(string userName, string password)
    {
        System.ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        System.ArgumentNullException.ThrowIfNull(password);

        return new(false, userName, password);
    }
}
