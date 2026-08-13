using System;
using TestFramework.Core.Steps;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql;
using TestFramework.Web.Sql.Artifacts;
using TestFramework.Web.Sql.Steps;
using TestFramework.Web.Sql.Steps.IsLive;

namespace TestFramework.Web;

/// <summary>
/// Creates SQL steps for a configured identifier.
/// </summary>
/// <remarks>
/// Rows are artifacts, because they have a key and a lifecycle. Statements that change data are
/// steps, because they are actions. Aggregates are observations.
/// </remarks>
public class SqlProxy
{
    /// <summary>
    /// Runs a statement that changes rows.
    /// </summary>
    /// <param name="identifier">The SQL identifier to run against.</param>
    /// <param name="statement">The statement text.</param>
    /// <returns>The step, ready for further <c>WithParameter</c> calls.</returns>
    public SqlExecuteStep Execute(SqlIdentifier identifier, string statement) => new(identifier, statement);

    /// <summary>
    /// Reads a single value from the database.
    /// </summary>
    /// <typeparam name="TValue">The scalar value type.</typeparam>
    /// <param name="identifier">The SQL identifier to run against.</param>
    /// <param name="statement">The statement text.</param>
    /// <returns>The step, ready for further <c>WithParameter</c> calls.</returns>
    public SqlScalarStep<TValue> Scalar<TValue>(SqlIdentifier identifier, string statement) => new(identifier, statement);

    /// <summary>
    /// Runs a script, batch by batch.
    /// </summary>
    /// <param name="identifier">The SQL identifier to run against.</param>
    /// <param name="script">The script to run.</param>
    /// <returns>The step, ready for further <c>WithParameter</c> calls.</returns>
    public SqlScriptStep Script(SqlIdentifier identifier, SqlScript script) => new(identifier, script);

    /// <summary>
    /// Probes whether the database is answering, using a constant level.
    /// </summary>
    /// <param name="identifier">The SQL identifier to probe.</param>
    /// <param name="level">How deep the probe should go.</param>
    public Step<SqlIsLiveResult> IsLive(SqlIdentifier identifier, SqlAlivenessLevel level = SqlAlivenessLevel.Database)
        => IsLive(identifier, Var.Const(level));

    /// <summary>
    /// Probes whether the database is answering, using a variable-backed level.
    /// </summary>
    /// <param name="identifier">The SQL identifier to probe.</param>
    /// <param name="level">The variable carrying the probe depth.</param>
    public Step<SqlIsLiveResult> IsLive(SqlIdentifier identifier, VariableReference<SqlAlivenessLevel> level)
        => new SqlIsLiveTrigger(identifier, level);
}

/// <summary>
/// Groups artifact reference factories.
/// </summary>
public class WebArtifactProxy
{
    /// <summary>
    /// Access SQL artifact reference factories.
    /// </summary>
    public SqlArtifactProxy Sql { get; } = new SqlArtifactProxy();
}

/// <summary>
/// Creates SQL artifact references.
/// </summary>
public class SqlArtifactProxy
{
    /// <summary>
    /// Creates a reference to a row the test owns, which is removed during teardown.
    /// </summary>
    /// <typeparam name="TRow">The row model type.</typeparam>
    /// <param name="identifier">The SQL identifier.</param>
    /// <param name="keyValues">The key values, in the key order declared by the model map.</param>
    public SqlRowArtifactReference<TRow> Row<TRow>(SqlIdentifier identifier, params VariableReference<string>[] keyValues)
        where TRow : class
        => new(identifier, keyValues);
}

/// <summary>
/// Groups artifact finder factories.
/// </summary>
public class WebArtifactFinderProxy
{
    /// <summary>
    /// Access SQL artifact finders.
    /// </summary>
    public SqlArtifactFinderProxy Sql { get; } = new SqlArtifactFinderProxy();
}

/// <summary>
/// Creates SQL artifact finders.
/// </summary>
public class SqlArtifactFinderProxy
{
    /// <summary>
    /// Finds rows matching a where clause. Located rows are observed, never deleted during teardown.
    /// </summary>
    /// <typeparam name="TRow">The row model type.</typeparam>
    /// <param name="identifier">The SQL identifier to query.</param>
    /// <param name="whereClause">The predicate, without the <c>WHERE</c> keyword.</param>
    /// <returns>The finder, ready for further <c>WithParameter</c> calls.</returns>
    public SqlRowWhereFinder<TRow> Where<TRow>(SqlIdentifier identifier, string whereClause)
        where TRow : class
        => new(identifier, whereClause);
}
