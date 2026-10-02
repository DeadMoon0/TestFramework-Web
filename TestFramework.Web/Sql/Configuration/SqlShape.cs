using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using TestFramework.Config.Configuration;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Exceptions;
using TestFramework.Web.Configuration;

namespace TestFramework.Web.Sql.Configuration;

/// <summary>
/// The <c>Sql</c> section, as the run's resource graph sees it.
/// </summary>
/// <remarks>
/// <para>
/// An entry may name a whole connection string or the parts one is built from, and the graph publishes the
/// composed result either way - what a reader wants is somewhere to connect, not a spelling. The composing is
/// the package's existing factory rather than a second implementation here.
/// </para>
/// <para>
/// An entry that holds neither publishes nothing rather than failing. That is the ordinary shape for a
/// container-provisioned database - it has a name before it has coordinates, and this package's own provider
/// deliberately accepts a name-only entry - so the case arrives through the default provider, not only
/// through a caller's own. The address the entry does not have is refused where something tries to connect,
/// by the factory.
/// </para>
/// </remarks>
/// <param name="provider">Reads an entry, including whatever validation the caller configured.</param>
internal sealed class SqlShape(ISqlConfigProvider provider) : ConfigShape<SqlConfig>
{
    /// <inheritdoc />
    public override string Section => DefaultSqlConfigProvider.SqlSelector;

    /// <inheritdoc />
    public override ResourceKind Kind => WebEnvironmentResourceKinds.SqlKind;

    /// <inheritdoc />
    public override IReadOnlyList<string> Identifiers(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadAllSqlIdentifier(configuration);
    }

    /// <inheritdoc />
    public override SqlConfig Read(IConfiguration configuration, string identifier)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadSqlConfig(configuration, identifier);
    }

    /// <inheritdoc />
    public override IReadOnlyDictionary<ValueKey, string> Values(SqlConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Dictionary<ValueKey, string> values = [];

        string? connectable = Connectable(config);
        if (connectable is { Length: > 0 } connectionString)
        {
            values[new ValueKey(ValueNames.ConnectionString, ResourceVantage.Host)] = connectionString;
            values[new ValueKey(ValueNames.ConnectionString, ResourceVantage.Network)] = connectionString;
        }

        // The entry's own Database first; for an entry written as one full connection string, the catalog that
        // string names. Publishing nothing there left a database-level probe nothing to verify against, although the
        // connection said which database it means.
        string? databaseName = config.Database is { Length: > 0 } stated
            ? stated
            : connectable is { Length: > 0 } ? new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectable).InitialCatalog : null;
        if (databaseName is { Length: > 0 } database)
        {
            values[new ValueKey(WebEnvironmentResourceKinds.DatabaseNameValue)] = database;
        }

        // The one property that is not already inside the composed string: it bounds a command rather than a
        // connection, so nothing would carry it otherwise.
        if (config.CommandTimeout is { } commandTimeout)
        {
            values[new ValueKey(WebEnvironmentResourceKinds.CommandTimeoutValue)] = commandTimeout.ToString("c", CultureInfo.InvariantCulture);
        }

        return values;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The record that comes back holds a connection string and not the parts it was composed from, which is
    /// the point rather than a loss: a server and a password are ingredients, and the composed string already
    /// contains them. Anything that connects takes the string; anything that wants to know which database it
    /// is takes the name.
    /// </para>
    /// <para>
    /// It also settles a rule structurally that used to need care. A connection string something started
    /// publishes wins over the declared one, and because the declared credentials were never separate values
    /// there is nothing left to layer on top of it - so a run cannot end up dialling a container with a
    /// developer's integrated security.
    /// </para>
    /// </remarks>
    public override SqlConfig Read(IReadOnlyDictionary<string, string> values, string identifier)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        SqlConfig config = new SqlConfig
        {
            ConnectionString = Text(values, ValueNames.ConnectionString),
            Database = Text(values, WebEnvironmentResourceKinds.DatabaseNameValue),
        };

        if (values.TryGetValue(WebEnvironmentResourceKinds.CommandTimeoutValue, out string? commandTimeout))
        {
            config = config with
            {
                CommandTimeout = ConfigValueRules.PositiveDuration(commandTimeout, out TimeSpan parsed)
                    ? parsed
                    : throw new FrameworkConfigurationException(
                        $"The run holds '{commandTimeout}' as {WebEnvironmentResourceKinds.CommandTimeoutValue} for database '{identifier}', which cannot be read as a positive duration such as '00:00:30'.")
                    {
                        RecoverySteps = [
                            $"Correct 'Sql:{identifier}:{WebEnvironmentResourceKinds.CommandTimeoutValue}', or the environment component that publishes it.",
                        ],
                    },
            };
        }

        return config;
    }

    private static string? Text(IReadOnlyDictionary<string, string> values, string valueName)
        => values.TryGetValue(valueName, out string? value) && value is { Length: > 0 } ? value : null;

    /// <summary>
    /// The connection string this entry amounts to, or null when it does not amount to one yet.
    /// </summary>
    /// <remarks>
    /// "Does this entry already say where the database is" is asked of the factory, which owns that rule,
    /// rather than answered by catching its refusal. The catch was tried and was wrong twice over: it named
    /// the wrong exception type, so a name-only entry crashed graph composition instead of publishing
    /// nothing - and once the type was right it would have swallowed real misconfigurations, reading a
    /// malformed connection string as "no coordinates yet". An entry with coordinates that still cannot
    /// compose is a load-time refusal with the factory's own message, which is what it always should have
    /// been.
    /// </remarks>
    private static string? Connectable(SqlConfig config)
    {
        if (!SqlConnectionStringFactory.HasCoordinates(config))
        {
            return null;
        }

        // The shape never learns the identifier - Values(config) is the whole signature - so the entry's
        // own database name is the best label a composition failure can carry.
        return SqlConnectionStringFactory.Create(config.Database is { Length: > 0 } database ? database : "the Sql entry", config);
    }
}
