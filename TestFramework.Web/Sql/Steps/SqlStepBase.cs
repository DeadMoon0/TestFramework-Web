using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Core.Environment;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql.Execution;

namespace TestFramework.Web.Sql.Steps;

/// <summary>
/// Shared behaviour for steps that run a statement against a configured database.
/// </summary>
/// <typeparam name="TResult">The step result type.</typeparam>
public abstract class SqlStepBase<TResult> : Step<TResult>, IHasEnvironmentRequirements
    where TResult : StepResultContext
{
    /// <summary>
    /// Creates a SQL step.
    /// </summary>
    /// <param name="sqlIdentifier">The SQL identifier to run against.</param>
    /// <param name="statement">The statement text.</param>
    /// <param name="parameters">The variable-backed parameters.</param>
    protected SqlStepBase(SqlIdentifier sqlIdentifier, string statement, SqlParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(sqlIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        ArgumentNullException.ThrowIfNull(parameters);

        SqlIdentifier = sqlIdentifier;
        Statement = statement;
        Parameters = parameters;
    }

    /// <summary>
    /// What the log says this step runs: the statement itself, unless it is made of data.
    /// </summary>
    /// <param name="config">The run's SQL tuning.</param>
    /// <returns>The text to log.</returns>
    private protected virtual string LoggedStatement(SqlStepConfig config) => Statement;

    /// <summary>
    /// The SQL identifier this step runs against.
    /// </summary>
    public SqlIdentifier SqlIdentifier { get; }

    /// <summary>
    /// The statement text.
    /// </summary>
    public string Statement { get; }

    /// <summary>
    /// The variable-backed parameters.
    /// </summary>
    protected SqlParameterSet Parameters { get; }

    /// <inheritdoc />
    public override bool DoesReturn => true;

    /// <inheritdoc />
    public IReadOnlyCollection<EnvironmentRequirement> GetEnvironmentRequirements(VariableStore variableStore)
        => [new EnvironmentRequirement(WebResourceKinds.Sql, SqlIdentifier)];

    /// <inheritdoc />
    public override void DeclareIO(StepIOContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        foreach (VariableReferenceGeneric input in Parameters.Inputs)
        {
            if (input.Identifier is { } identifier)
                contract.Input(identifier, typeof(object));
        }
    }

    /// <summary>
    /// Resolves the executor, the step configuration and the parameters, and logs the statement.
    /// </summary>
    /// <remarks>
    /// One context rather than three of its pieces. The executor is now built per run - a database this run
    /// started has a connection string that no service provider can reach - so the run has to arrive here,
    /// and once it does the other two parameters were only ever pieces of it.
    /// </remarks>
    /// <param name="context">The run.</param>
    /// <returns>The executor, the step configuration it was logged under, and the resolved parameters.</returns>
    protected (ISqlExecutor Executor, SqlStepConfig StepConfig, IReadOnlyDictionary<string, object?> Parameters) Prepare(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        VariableStore variableStore = context.Variables;
        ScopedLogger logger = context.Logger;

        ISqlExecutor executor = SqlConfigResolver.ResolveExecutor(context);
        SqlStepConfig stepConfig = context.Services.GetService<SqlStepConfig>() ?? new SqlStepConfig();
        IReadOnlyDictionary<string, object?> parameters = Parameters.Resolve(variableStore);

        if (stepConfig.LogStatements)
        {
            string described = stepConfig.LogParameterValues
                ? string.Join(", ", parameters.Select(parameter => $"{parameter.Key}={parameter.Value}"))
                : string.Join(", ", parameters.Keys);

            logger.LogInformation(
                "SQL '{0}' -> {1} [{2}]",
                SqlIdentifier.ToString(),
                LoggedStatement(stepConfig),
                described);
        }

        return (executor, stepConfig, parameters);
    }
}
