using System;
using Microsoft.Data.SqlClient;
using TestFramework.Web.Sql.Exceptions;

namespace TestFramework.Web.Sql;

/// <summary>
/// Composes connection strings from a <see cref="SqlConfig"/> and optional run-time credentials.
/// </summary>
public static class SqlConnectionStringFactory
{
    /// <summary>
    /// Whether the entry says where the database is at all.
    /// </summary>
    /// <remarks>
    /// The question a shape asks before composing: an entry that only names a database - a complete thing
    /// to say about one a container has yet to create - has no coordinates yet and publishes nothing,
    /// which is an answer rather than a failure. It lives here beside the refusal in
    /// <see cref="Create(string, SqlConfig, SqlCredentials)"/> so the two cannot come to different conclusions about what "has an address"
    /// means; asking by catching the refusal was tried and swallowed real misconfigurations - a malformed
    /// connection string read as "no coordinates yet".
    /// </remarks>
    /// <param name="config">The entry.</param>
    /// <returns>True when composing can be asked to succeed.</returns>
    public static bool HasCoordinates(SqlConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return !string.IsNullOrWhiteSpace(config.ConnectionString) || !string.IsNullOrWhiteSpace(config.Server);
    }

    /// <summary>
    /// Builds the connection string for an identifier.
    /// </summary>
    /// <remarks>Uses <c>null</c> for <c>credentials</c>; the other overload states it.</remarks>
    /// <param name="identifier">The SQL identifier being resolved, used for error messages.</param>
    /// <param name="config">The configuration to compose from.</param>
    /// <returns>The composed connection string.</returns>
    public static string Create(string identifier, SqlConfig config)
        => Create(identifier, config, null);

    /// <summary>
    /// Builds the connection string for an identifier.
    /// </summary>
    /// <param name="identifier">The SQL identifier being resolved, used for error messages.</param>
    /// <param name="config">The configuration to compose from.</param>
    /// <param name="credentials">Credentials that override the configured ones, when supplied.</param>
    /// <returns>The composed connection string.</returns>
    public static string Create(string identifier, SqlConfig config, SqlCredentials? credentials)
    {
        ArgumentNullException.ThrowIfNull(config);

        SqlConnectionStringBuilder builder = string.IsNullOrWhiteSpace(config.ConnectionString)
            ? CreateFromParts(identifier, config)
            : CreateFromConnectionString(identifier, config.ConnectionString!);

        ApplyCredentials(builder, config, credentials);

        if (config.TrustServerCertificate)
            builder.TrustServerCertificate = true;

        if (config.Encrypt is { } encrypt)
            builder.Encrypt = encrypt;

        if (config.ConnectTimeout is { } connectTimeout)
            builder.ConnectTimeout = (int)connectTimeout.TotalSeconds;

        return builder.ConnectionString;
    }

    /// <summary>
    /// Returns a log-safe description of a connection: server and database only, never credentials.
    /// </summary>
    /// <param name="connectionString">The connection string to describe.</param>
    public static string Describe(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        SqlConnectionStringBuilder builder = new(connectionString);
        return $"{builder.DataSource}/{builder.InitialCatalog}";
    }

    private static SqlConnectionStringBuilder CreateFromParts(string identifier, SqlConfig config)
    {
        // The only place the absence is a failure rather than a fact, so it is the only place that says so.
        // Both roads are named because both are normal: a person writes the address down, or the thing that
        // creates the database publishes it while starting.
        if (string.IsNullOrWhiteSpace(config.Server))
            throw SqlConfigurationValidationException.InvalidValue(
                identifier,
                nameof(SqlConfig.Server),
                "the run holds no address for it. Set 'Sql:" + identifier + ":ConnectionString' or 'Sql:" + identifier
                    + ":Server', or include the definition that provisions the database so it publishes one when it starts");

        if (string.IsNullOrWhiteSpace(config.Database))
            throw SqlConfigurationValidationException.InvalidValue(identifier, nameof(SqlConfig.Database), "no Database and no ConnectionString were configured");

        return new SqlConnectionStringBuilder
        {
            DataSource = config.Server,
            InitialCatalog = config.Database,
        };
    }

    private static SqlConnectionStringBuilder CreateFromConnectionString(string identifier, string connectionString)
    {
        try
        {
            return new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            // The message deliberately omits the value: a malformed connection string usually still
            // carries a password.
            throw SqlConfigurationValidationException.InvalidValue(
                identifier,
                nameof(SqlConfig.ConnectionString),
                $"the value could not be parsed ({exception.GetType().Name})");
        }
    }

    private static void ApplyCredentials(SqlConnectionStringBuilder builder, SqlConfig config, SqlCredentials? credentials)
    {
        bool integrated = credentials?.IntegratedSecurity ?? config.IntegratedSecurity;
        string? userName = credentials is null ? config.UserName : credentials.UserName;
        string? password = credentials is null ? config.Password : credentials.Password;

        if (integrated)
        {
            builder.IntegratedSecurity = true;
            builder.Remove("User ID");
            builder.Remove("Password");
            return;
        }

        if (string.IsNullOrWhiteSpace(userName))
            return;

        builder.IntegratedSecurity = false;
        builder.UserID = userName;
        builder.Password = password ?? string.Empty;
    }
}
