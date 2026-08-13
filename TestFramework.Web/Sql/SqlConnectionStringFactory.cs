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
    /// Builds the connection string for an identifier.
    /// </summary>
    /// <param name="identifier">The SQL identifier being resolved, used for error messages.</param>
    /// <param name="config">The configuration to compose from.</param>
    /// <param name="credentials">Credentials that override the configured ones, when supplied.</param>
    /// <returns>The composed connection string.</returns>
    public static string Create(string identifier, SqlConfig config, SqlCredentials? credentials = null)
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
        if (string.IsNullOrWhiteSpace(config.Server))
            throw SqlConfigurationValidationException.InvalidValue(identifier, nameof(SqlConfig.Server), "no Server and no ConnectionString were configured");

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
