using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using TestFramework.Config.Configuration;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Exceptions;

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
/// An entry that holds neither publishes nothing rather than failing. Note that this package's own provider
/// does not currently allow such an entry - it refuses a section with no <c>Server</c> and no
/// <c>ConnectionString</c> while loading, before a shape sees it - so the guard is for a caller who supplied a
/// provider that permits one. It is also the shape a container-provisioned database wants, which is worth
/// knowing when the producing half of the graph arrives: a database something is about to create has a name
/// before it has coordinates.
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

        if (Connectable(config) is { Length: > 0 } connectionString)
        {
            values[new ValueKey(ValueNames.ConnectionString, ResourceVantage.Host)] = connectionString;
            values[new ValueKey(ValueNames.ConnectionString, ResourceVantage.Network)] = connectionString;
        }

        if (config.Database is { Length: > 0 } database)
        {
            values[new ValueKey(WebEnvironmentResourceKinds.DatabaseNameValue)] = database;
        }

        return values;
    }

    /// <summary>
    /// The connection string this entry amounts to, or null when it does not amount to one yet.
    /// </summary>
    /// <remarks>
    /// The factory refuses an entry with neither a connection string nor a server and database, and refusing
    /// is right when something is about to connect. Here it is a question rather than a demand - "does this
    /// entry already say where the database is" - so the refusal is an answer of no, not a failure of the run.
    /// Unreachable through this package's default provider, which refuses such an entry earlier; reachable
    /// through a caller's own.
    /// </remarks>
    private static string? Connectable(SqlConfig config)
    {
        try
        {
            return SqlConnectionStringFactory.Create("configuration", config);
        }
        catch (FrameworkConfigurationException)
        {
            return null;
        }
    }
}
