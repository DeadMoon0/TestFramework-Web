using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql.Execution;
using System.ComponentModel;

namespace TestFramework.Web.Sql.Steps;

/// <summary>
/// Result of a statement that changes rows.
/// </summary>
public sealed record SqlExecuteResult : StepResultContext
{
    internal SqlExecuteResult(string sqlIdentifier, int affectedRows, TimeSpan elapsed)
    {
        this.SqlIdentifier = sqlIdentifier;
        this.AffectedRows = affectedRows;
        this.Elapsed = elapsed;
    }

    /// <summary>
    /// The database the statement ran against.
    /// </summary>
    public string SqlIdentifier { get; }

    /// <summary>
    /// The number of rows the statement changed.
    /// </summary>
    public int AffectedRows { get; }

    /// <summary>
    /// Wall-clock duration of the statement.
    /// </summary>
    public TimeSpan Elapsed { get; }

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
    internal SqlExecuteStep(SqlIdentifier sqlIdentifier, string statement, SqlParameterSet? parameters)
        : base(sqlIdentifier, statement, parameters ?? new SqlParameterSet())
    {
    }

    /// <inheritdoc />
    public override string Name => "SQL Execute";

    /// <inheritdoc />
    public override string Description => $"Runs a statement against the database '{SqlIdentifier}'";

    /// <summary>
    /// Binds a parameter to a value.
    /// </summary>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The value.</param>
    /// <returns>The same instance, for chaining.</returns>
    public SqlExecuteStep WithParameter<TValue>(string name, TValue value)
        => WithParameterFrom(name, Var.Const(NotAValue.Require(value, $"WithParameter('{name}')", $"WithParameterFrom('{name}', ...)")));

    /// <summary>
    /// Binds a parameter to a variable, read when the step runs.
    /// </summary>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The variable carrying the value.</param>
    /// <returns>The same instance, for chaining.</returns>
    public SqlExecuteStep WithParameterFrom<TValue>(string name, VariableReference<TValue> value)
{
        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        Parameters.Add(name, value);
        return this;
    }

    /// <summary>Binds a parameter to a constant, which is a value.</summary>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The constant.</param>
    /// <returns>The same instance, for chaining.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SqlExecuteStep WithParameter<TValue>(string name, ConstVariable<TValue> value) => WithParameterFrom(name, value);


    /// <inheritdoc />
    public override Step<SqlExecuteResult> Clone()
        => new SqlExecuteStep(SqlIdentifier, Statement, Parameters.Clone()).WithClonedOptions(this);

    /// <inheritdoc />
    public override StepInstance<Step<SqlExecuteResult>, SqlExecuteResult> GetInstance() => new(this);

    /// <inheritdoc />
    public override async Task<SqlExecuteResult?> Execute(RunContext context)
    {
        (ISqlExecutor executor, IReadOnlyDictionary<string, object?> parameters) = Prepare(context);

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int affected = await executor.ExecuteAsync(SqlIdentifier, Statement, parameters, context.Deadline.Token).ConfigureAwait(false);
        stopwatch.Stop();

        context.Logger.LogInformation("SQL '{0}' <- {1} row(s) in {2}", SqlIdentifier.ToString(), affected, stopwatch.Elapsed);
        return new SqlExecuteResult(SqlIdentifier, affected, stopwatch.Elapsed);
    }
}
