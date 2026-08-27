using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Steps;
using TestFramework.Web.Configuration;
using TestFramework.Web.Sql.Exceptions;

namespace TestFramework.Web.Sql;

/// <summary>
/// Where a database actually is, whoever supplied the answer.
/// </summary>
/// <remarks>
/// <para>
/// A configured database has a server somebody wrote down. One this run started in a container answers on a
/// port the operating system chose. Both arrive as resource values, so this asks the run once and callers
/// stop having to know which happened - the reason a starting container used to write its connection string
/// back into this package's configuration store.
/// </para>
/// <para>
/// It takes the run rather than a service provider, which is what makes that possible: a value produced by
/// something this run started exists on the run and nowhere else.
/// </para>
/// </remarks>
public static class SqlConfigResolver
{
    /// <summary>
    /// The database's configuration, with whatever the run knows about where it is filled in.
    /// </summary>
    /// <param name="context">The run.</param>
    /// <param name="identifier">The SQL identifier to resolve.</param>
    /// <returns>The resolved configuration.</returns>
    /// <exception cref="SqlConfigurationValidationException">Neither the run nor any configuration entry knows this database.</exception>
    public static SqlConfig Resolve(RunContext context, string identifier)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        SqlConfig? declared = Declared(context.Services, identifier);
        ResolvedValue? connection = Connection(context, identifier);
        string? database = Value(context, identifier, WebEnvironmentResourceKinds.DatabaseNameValue);

        if (connection?.Origin == ValueOrigin.Produced)
        {
            // Something this run started supplied the whole connection, so it is taken whole. A container
            // owns its port *and* its credentials, and the rest of an entry describes reaching a database
            // somewhere else: a developer's integrated-security flag applied on top of this string would
            // strip out the very user and password the container just handed over.
            //
            // The timeouts stay, because they are the caller's patience rather than a way of connecting.
            return (declared ?? new SqlConfig()) with
            {
                ConnectionString = connection.Value,
                Database = database ?? declared?.Database,
                Server = null,
                IntegratedSecurity = false,
                UserName = null,
                Password = null,
            };
        }

        if (declared is null)
        {
            // Nothing wrote this database down and nothing started one either.
            throw SqlConfigurationValidationException.MissingIdentifier(identifier, KnownIdentifiers(context.Services));
        }

        // A declared coordinate is a person's description of a database they cannot see, so the rest of their
        // entry still qualifies it - which is why this is a `with` rather than a replacement.
        return declared with
        {
            ConnectionString = connection?.Value ?? declared.ConnectionString,
            Database = database ?? declared.Database,
        };
    }

    /// <summary>
    /// Resolves the executor for a run, defaulting to the ADO implementation.
    /// </summary>
    /// <param name="context">The run.</param>
    /// <remarks>
    /// <para>
    /// The run is passed to the executor rather than through <see cref="Execution.ISqlExecutor"/>'s methods,
    /// and that is the whole reason the seam did not have to change: an executor is built per run and holds
    /// it, so <c>ExecuteAsync(identifier, statement, parameters, token)</c> keeps its exact shape and an
    /// implementation somebody else wrote keeps working unchanged.
    /// </para>
    /// <para>
    /// Such an implementation resolves its own connection strings, though, and one that reads
    /// <c>WebConfigStore&lt;SqlConfig&gt;</c> sees what was declared and not what a container produced. That
    /// is the caller's choice to make; the shipped executor reads the run.
    /// </para>
    /// </remarks>
    public static Execution.ISqlExecutor ResolveExecutor(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Services.GetService<Execution.ISqlExecutor>() ?? new Execution.AdoSqlExecutor(context);
    }

    /// <summary>
    /// What somebody wrote down, which is still the answer for a database nothing in this run started.
    /// </summary>
    /// <remarks>
    /// Null rather than a refusal, because "nobody configured this" is not yet an error: a database the run
    /// provisioned answers from the run's resources instead. The refusal belongs where both have been asked.
    /// </remarks>
    private static SqlConfig? Declared(IServiceProvider serviceProvider, string identifier)
    {
        WebConfigStore<SqlConfig>? store = serviceProvider.GetService<WebConfigStore<SqlConfig>>();

        return store is not null && store.TryGetConfig(identifier, out SqlConfig? config) ? config : null;
    }

    /// <summary>
    /// The databases that were configured, so a failure can list them.
    /// </summary>
    private static IReadOnlyCollection<string> KnownIdentifiers(IServiceProvider serviceProvider)
        => serviceProvider.GetService<WebConfigStore<SqlConfig>>() is { } store ? [.. store.Snapshot().Keys] : [];

    /// <summary>
    /// The connection string the run holds, with where it came from.
    /// </summary>
    /// <remarks>
    /// The origin is the whole reason this one is read differently from the others: it decides whether the
    /// value is complete on its own or is qualified by the entry around it.
    /// </remarks>
    private static ResolvedValue? Connection(RunContext context, string identifier)
        => context.Values.TryResolve(
            ValueRef.For(WebEnvironmentResourceKinds.Sql, identifier, ValueNames.ConnectionString),
            ResourceVantage.Host,
            out ResolvedValue? resolved)
            ? resolved
            : null;

    /// <summary>
    /// One value from the run, from the viewpoint of the process asking.
    /// </summary>
    /// <remarks>
    /// The host viewpoint, because this runs in the test process. Code inside the network asks for the same
    /// value name from its own viewpoint and gets the address that works there.
    /// </remarks>
    private static string? Value(RunContext context, string identifier, string valueName)
        => context.Values.TryGet(
            ValueRef.For(WebEnvironmentResourceKinds.Sql, identifier, valueName),
            ResourceVantage.Host,
            out string? value)
            ? value
            : null;

    /// <summary>
    /// Resolves the model registry for a run, defaulting to attributes and convention only.
    /// </summary>
    /// <remarks>
    /// Stays on the service provider, and correctly: a model registry maps row types to tables and does not
    /// depend on which run is asking. §7's rule cuts both ways - what needs the run takes it, what does not
    /// must not pretend to.
    /// </remarks>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    public static Model.SqlModelRegistry ResolveModelRegistry(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return serviceProvider.GetService<Model.SqlModelRegistry>() ?? Model.SqlModelRegistry.CreateDefault();
    }
}
