using TestFramework.Core.Steps;

namespace TestFramework.Web.Sql.Execution;

/// <summary>
/// Creates the <see cref="ISqlExecutor"/> a SQL step runs with, for the run that step belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The seam a different data access library plugs into, registered with <c>UseSqlExecutor(factory)</c>. It is a
/// factory handed the step's <see cref="RunContext"/>, not an executor registered as a service, because an executor
/// has to see the run it executes for: where a database is - a container's published connection string above all -
/// exists only in the run, through <c>context.Configured&lt;SqlConfig&gt;(identifier)</c>. A singleton executor in the
/// service provider was handed nothing and could never resolve it. The HTTP lane's <c>IWebComponentFactory</c> has
/// the same shape for the same reason.
/// </para>
/// <para>
/// What an executor it creates must do, because Web's own steps rely on it:
/// </para>
/// <list type="bullet">
/// <item>Resolve the connection for an identifier from the context it was created with, and describe it in
/// <see cref="ISqlExecutor.Describe"/> without credentials.</item>
/// <item>Honour the cancellation token on every call; it carries the step's deadline.</item>
/// <item><see cref="ISqlExecutor.QueryAsync{TRow}"/> maps columns to properties by name: Web's statements alias every
/// column to its model property (<c>[order_id] AS [Id]</c>).</item>
/// <item><see cref="ISqlExecutor.ScalarAsync{TValue}"/> converts the first column of the first row to the asked type:
/// Web reads <c>COUNT(1)</c> as <c>int</c>, and <c>SELECT 1</c> and <c>DB_NAME()</c> as <c>string</c>.</item>
/// <item><see cref="ISqlExecutor.ExecuteScriptAsync"/> runs a script's batches on one connection, so temp tables,
/// <c>SET</c> options and transactions survive a <c>GO</c>.</item>
/// </list>
/// </remarks>
public interface ISqlExecutorFactory
{
    /// <summary>
    /// Creates the executor for one step.
    /// </summary>
    /// <param name="context">The context of the step about to run.</param>
    /// <returns>The executor.</returns>
    ISqlExecutor Create(RunContext context);
}
