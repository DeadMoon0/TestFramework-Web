using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql.Execution;

namespace TestFramework.Web.Sql.Steps;

/// <summary>
/// Result of a scalar query.
/// </summary>
/// <typeparam name="TValue">The scalar value type.</typeparam>
/// <param name="SqlIdentifier">The database the query ran against.</param>
/// <param name="Value">The scalar value.</param>
/// <param name="Elapsed">Wall-clock duration of the query.</param>
public sealed record SqlScalarResult<TValue>(string SqlIdentifier, TValue? Value, TimeSpan Elapsed) : StepResultContext
{
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
    public SqlScalarStep(SqlIdentifier sqlIdentifier, string statement, SqlParameterSet? parameters = null)
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
    /// Binds a parameter used by the statement.
    /// </summary>
    /// <typeparam name="TParameter">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The variable carrying the value.</param>
    public SqlScalarStep<TValue> WithParameter<TParameter>(string name, VariableReference<TParameter> value)
    {
        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        Parameters.Add(name, value);
        return this;
    }

    /// <inheritdoc />
    public override Step<SqlScalarResult<TValue>> Clone()
        => new SqlScalarStep<TValue>(SqlIdentifier, Statement, Parameters.Clone()).WithClonedOptions(this);

    /// <inheritdoc />
    public override StepInstance<Step<SqlScalarResult<TValue>>, SqlScalarResult<TValue>> GetInstance() => new(this);

    /// <inheritdoc />
    public override async Task<SqlScalarResult<TValue>?> Execute(
        IServiceProvider serviceProvider,
        VariableStore variableStore,
        ArtifactStore artifactStore,
        ScopedLogger logger,
        CancellationToken cancellationToken)
    {
        (ISqlExecutor executor, IReadOnlyDictionary<string, object?> parameters) = Prepare(serviceProvider, variableStore, logger);

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        TValue? value = await executor.ScalarAsync<TValue>(SqlIdentifier, Statement, parameters, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        logger.LogInformation("SQL '{0}' <- {1} in {2}", SqlIdentifier.ToString(), value?.ToString() ?? "null", stopwatch.Elapsed);
        return new SqlScalarResult<TValue>(SqlIdentifier, value, stopwatch.Elapsed);
    }
}
