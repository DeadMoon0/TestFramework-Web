using System;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config;
using TestFramework.Core.Steps;

namespace TestFramework.Web.Sql;

/// <summary>
/// The machinery a SQL step needs beside its configuration.
/// </summary>
/// <remarks>
/// <para>
/// What is left here after configuration moved onto the run: two lookups, and neither is configuration. The
/// record itself is <c>context.Configured&lt;SqlConfig&gt;(identifier)</c> now, the one reader the family
/// shares.
/// </para>
/// <para>
/// A whole branch went with it, and that is the interesting part. This used to read a value's <em>origin</em>
/// to decide whether a produced connection string should be taken whole or qualified by the entry's declared
/// credentials — because a container's connection string with a developer's integrated security layered on
/// top dials the wrong database. That decision no longer exists to make: a server, a user name and a password
/// are ingredients the shape composes into the connection string before publishing it, so they are never
/// separate values and there is nothing to layer. The credentials that apply are the ones inside whichever
/// string won.
/// </para>
/// </remarks>
public static class SqlConfigResolver
{
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
    /// Such an implementation resolves its own connection strings, and one that reaches for a configuration
    /// store finds nothing there now. That is the caller's choice to make; the shipped executor reads the run.
    /// </para>
    /// </remarks>
    public static Execution.ISqlExecutor ResolveExecutor(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // An executor registered as a service was a singleton handed nothing, so it could never see the run it
        // executes for. Refused by name rather than ignored: ignoring it would silently swap the author's
        // executor for the default one.
        if (context.Services.GetService<Execution.ISqlExecutor>() is { } registered)
        {
            throw new Core.Exceptions.FrameworkConfigurationException(
                $"An ISqlExecutor ('{registered.GetType().Name}') is registered as a service, and Web creates executors through ISqlExecutorFactory, which is handed the run.")
            {
                RecoverySteps =
                [
                    "Implement ISqlExecutorFactory, create the executor in Create(context), and register it with UseSqlExecutor(factory).",
                    "Resolve connections inside the executor with context.Configured<SqlConfig>(identifier), so a container-published database is found.",
                ],
            };
        }

        return context.Services.GetService<Execution.ISqlExecutorFactory>() is { } factory
            ? factory.Create(context)
            : new Execution.AdoSqlExecutor(context);
    }

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
