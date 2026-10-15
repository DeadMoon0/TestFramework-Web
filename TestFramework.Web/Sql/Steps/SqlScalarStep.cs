using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql.Execution;
using System.ComponentModel;

namespace TestFramework.Web.Sql.Steps;

/// <summary>
/// Result of a scalar query.
/// </summary>
/// <typeparam name="TValue">The scalar value type.</typeparam>
public sealed record SqlScalarResult<TValue> : StepResultContext
{
    internal SqlScalarResult(string sqlIdentifier, TValue? value, TimeSpan elapsed)
    {
        this.SqlIdentifier = sqlIdentifier;
        this.Value = value;
        this.Elapsed = elapsed;
    }

    /// <summary>
    /// The database the query ran against.
    /// </summary>
    public string SqlIdentifier { get; }

    /// <summary>
    /// The scalar value.
    /// </summary>
    public TValue? Value { get; }

    /// <summary>
    /// Wall-clock duration of the query.
    /// </summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// Returns a readable description of the outcome.
    /// </summary>
    public override string ToString() => $"'{SqlIdentifier}' returned {Value?.ToString() ?? "null"} in {Elapsed:g}";
}

/// <summary>
/// Reads a single value from the database.
/// </summary>
/// <typeparam name="TValue">The scalar value type.</typeparam>
/// <remarks>
/// An aggregate has no identity and no lifecycle, so it is an observation rather than an artifact.
/// </remarks>
public sealed class SqlScalarStep<TValue> : SqlStepBase<SqlScalarResult<TValue>>
{
    /// <summary>
    /// Creates the step.
    /// </summary>
    /// <param name="sqlIdentifier">The SQL identifier to run against.</param>
    /// <param name="statement">The statement text.</param>
    /// <param name="parameters">The variable-backed parameters.</param>
    internal SqlScalarStep(SqlIdentifier sqlIdentifier, string statement, SqlParameterSet? parameters)
        : base(sqlIdentifier, statement, parameters ?? new SqlParameterSet())
    {
    }

    /// <inheritdoc />
    public override string Name => "SQL Scalar";

    /// <inheritdoc />
    public override string Description => $"Reads a single value from the database '{SqlIdentifier}'";

    /// <inheritdoc />
    public override StepExecutionPhase Phase => StepExecutionPhase.Observe;

    /// <summary>
    /// Binds a parameter to a value.
    /// </summary>
    /// <typeparam name="TParameter">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The value.</param>
    /// <returns>The same instance, for chaining.</returns>
    public SqlScalarStep<TValue> WithParameter<TParameter>(string name, TParameter value)
        => WithParameterFrom(name, Var.Const(NotAValue.Require(value, $"WithParameter('{name}')", $"WithParameterFrom('{name}', ...)")));

    /// <summary>
    /// Binds a parameter to a variable, read when the step runs.
    /// </summary>
    /// <typeparam name="TParameter">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The variable carrying the value.</param>
    /// <returns>The same instance, for chaining.</returns>
    public SqlScalarStep<TValue> WithParameterFrom<TParameter>(string name, VariableReference<TParameter> value)
{
        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        Parameters.Add(name, value);
        return this;
    }

    /// <summary>Binds a parameter to a constant, which is a value.</summary>
    /// <typeparam name="TParameter">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The constant.</param>
    /// <returns>The same instance, for chaining.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SqlScalarStep<TValue> WithParameter<TParameter>(string name, ConstVariable<TParameter> value) => WithParameterFrom(name, value);


    /// <inheritdoc />
    public override Step<SqlScalarResult<TValue>> Clone()
        => new SqlScalarStep<TValue>(SqlIdentifier, Statement, Parameters.Clone()).WithClonedOptions(this);

    /// <inheritdoc />
    public override StepInstance<Step<SqlScalarResult<TValue>>, SqlScalarResult<TValue>> GetInstance() => new(this);

    /// <inheritdoc />
    public override async Task<SqlScalarResult<TValue>?> Execute(RunContext context)
    {
        (ISqlExecutor executor, IReadOnlyDictionary<string, object?> parameters) = Prepare(context);

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        TValue? value = await executor.ScalarAsync<TValue>(SqlIdentifier, Statement, parameters, context.Deadline.Token).ConfigureAwait(false);
        stopwatch.Stop();

        // Gated the way parameter values are, and for the same reason: a scalar read is real data - a
        // secret or a personal column lands in the log otherwise, with no opt-out. The value still reaches
        // the result and the run; only the log line withholds it.
        SqlStepConfig stepConfig = context.Services.GetService<SqlStepConfig>() ?? new SqlStepConfig();
        string logged = stepConfig.LogParameterValues
            ? value?.ToString() ?? "null"
            : $"({typeof(TValue).Name}; enable {nameof(SqlStepConfig)}.{nameof(SqlStepConfig.LogParameterValues)} to log values)";

        context.Logger.LogInformation("SQL '{0}' <- {1} in {2}", SqlIdentifier.ToString(), logged, stopwatch.Elapsed);
        return new SqlScalarResult<TValue>(SqlIdentifier, value, stopwatch.Elapsed);
    }
}
