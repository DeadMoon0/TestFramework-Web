using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql.Execution;

namespace TestFramework.Web.Sql.Steps;

/// <summary>
/// Result of a statement that changes rows.
/// </summary>
/// <param name="SqlIdentifier">The database the statement ran against.</param>
/// <param name="AffectedRows">The number of rows the statement changed.</param>
/// <param name="Elapsed">Wall-clock duration of the statement.</param>
public sealed record SqlExecuteResult(string SqlIdentifier, int AffectedRows, TimeSpan Elapsed) : StepResultContext
{
    /// <summary>
    /// Returns a readable description of the outcome.
    /// </summary>
    public override string ToString() => $"'{SqlIdentifier}' affected {AffectedRows} row(s) in {Elapsed:g}";
}

/// <summary>
/// Runs a statement that changes rows.
/// </summary>
public sealed class SqlExecuteStep : SqlStepBase<SqlExecuteResult>
{
    /// <summary>
    /// Creates the step.
    /// </summary>
    /// <param name="sqlIdentifier">The SQL identifier to run against.</param>
    /// <param name="statement">The statement text.</param>
    /// <param name="parameters">The variable-backed parameters.</param>
    public SqlExecuteStep(SqlIdentifier sqlIdentifier, string statement, SqlParameterSet? parameters = null)
        : base(sqlIdentifier, statement, parameters ?? new SqlParameterSet())
    {
    }

    /// <inheritdoc />
    public override string Name => "SQL Execute";

    /// <inheritdoc />
    public override string Description => $"Runs a statement against the database '{SqlIdentifier}'";

    /// <summary>
    /// Binds a parameter used by the statement.
    /// </summary>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The variable carrying the value.</param>
    public SqlExecuteStep WithParameter<TValue>(string name, VariableReference<TValue> value)
    {
        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        Parameters.Add(name, value);
        return this;
    }

    /// <inheritdoc />
    public override Step<SqlExecuteResult> Clone()
        => new SqlExecuteStep(SqlIdentifier, Statement, Parameters.Clone()).WithClonedOptions(this);

    /// <inheritdoc />
    public override StepInstance<Step<SqlExecuteResult>, SqlExecuteResult> GetInstance() => new(this);

    /// <inheritdoc />
    public override async Task<SqlExecuteResult?> Execute(
        IServiceProvider serviceProvider,
        VariableStore variableStore,
        ArtifactStore artifactStore,
        ScopedLogger logger,
        CancellationToken cancellationToken)
    {
        (ISqlExecutor executor, IReadOnlyDictionary<string, object?> parameters) = Prepare(serviceProvider, variableStore, logger);

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int affected = await executor.ExecuteAsync(SqlIdentifier, Statement, parameters, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        logger.LogInformation("SQL '{0}' <- {1} row(s) in {2}", SqlIdentifier.ToString(), affected, stopwatch.Elapsed);
        return new SqlExecuteResult(SqlIdentifier, affected, stopwatch.Elapsed);
    }
}
