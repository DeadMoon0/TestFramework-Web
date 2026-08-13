using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Environment;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql.Execution;

namespace TestFramework.Web.Sql.Steps.IsLive;

/// <summary>
/// Depth of a SQL liveness probe.
/// </summary>
public enum SqlAlivenessLevel
{
    /// <summary>
    /// The server accepts a connection with the configured credentials.
    /// </summary>
    Reachable = 0,

    /// <summary>
    /// The configured database can be opened and queried.
    /// </summary>
    Database = 1,
}

/// <summary>
/// Result of a SQL liveness probe.
/// </summary>
/// <param name="SqlIdentifier">The database that was probed.</param>
/// <param name="AlivenessLevel">The requested probe depth.</param>
/// <param name="Connection">A log-safe description of the connection.</param>
/// <param name="Success">Whether the probe succeeded.</param>
/// <param name="Elapsed">Wall-clock duration of the probe.</param>
public sealed record SqlIsLiveResult(string SqlIdentifier, SqlAlivenessLevel AlivenessLevel, string Connection, bool Success, TimeSpan Elapsed) : StepResultContext
{
    /// <summary>
    /// Returns a readable description of the outcome.
    /// </summary>
    public override string ToString() => $"'{SqlIdentifier}' {AlivenessLevel} probe of {Connection} in {Elapsed:g}";
}

/// <summary>
/// Probes whether a configured database is answering.
/// </summary>
public sealed class SqlIsLiveTrigger : Step<SqlIsLiveResult>, IHasEnvironmentRequirements
{
    private readonly SqlIdentifier _sqlIdentifier;
    private readonly VariableReference<SqlAlivenessLevel> _alivenessLevel;

    /// <summary>
    /// Creates the probe.
    /// </summary>
    /// <param name="sqlIdentifier">The SQL identifier to probe.</param>
    /// <param name="alivenessLevel">How deep the probe should go.</param>
    public SqlIsLiveTrigger(SqlIdentifier sqlIdentifier, VariableReference<SqlAlivenessLevel> alivenessLevel)
    {
        ArgumentNullException.ThrowIfNull(sqlIdentifier);
        ArgumentNullException.ThrowIfNull(alivenessLevel);

        _sqlIdentifier = sqlIdentifier;
        _alivenessLevel = alivenessLevel;
    }

    /// <inheritdoc />
    public override string Name => "SQL IsLive Trigger";

    /// <inheritdoc />
    public override string Description => $"Checks whether the database '{_sqlIdentifier}' is answering";

    /// <inheritdoc />
    public override bool DoesReturn => true;

    /// <inheritdoc />
    public override StepExecutionPhase Phase => StepExecutionPhase.Observe;

    /// <inheritdoc />
    public override Step<SqlIsLiveResult> Clone() => new SqlIsLiveTrigger(_sqlIdentifier, _alivenessLevel).WithClonedOptions(this);

    /// <inheritdoc />
    public override StepInstance<Step<SqlIsLiveResult>, SqlIsLiveResult> GetInstance() => new(this);

    /// <inheritdoc />
    public override void DeclareIO(StepIOContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (_alivenessLevel.Identifier is { } identifier)
            contract.Inputs.Add(new StepIOEntry(identifier.Identifier, StepIOKind.Variable, false, typeof(SqlAlivenessLevel)));
    }

    /// <inheritdoc />
    public IReadOnlyCollection<EnvironmentRequirement> GetEnvironmentRequirements(VariableStore variableStore)
        => [new EnvironmentRequirement(WebEnvironmentResourceKinds.Sql, _sqlIdentifier)];

    /// <inheritdoc />
    public override async Task<SqlIsLiveResult?> Execute(
        IServiceProvider serviceProvider,
        VariableStore variableStore,
        ArtifactStore artifactStore,
        ScopedLogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        SqlAlivenessLevel level = _alivenessLevel.GetValue(variableStore);
        ISqlExecutor executor = SqlConfigResolver.ResolveExecutor(serviceProvider);
        string connection = executor.Describe(_sqlIdentifier);

        logger.LogInformation("SQL IsLive '{0}' probing {1} at level {2}.", _sqlIdentifier.ToString(), connection, level);

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Reachable proves the connection opens at all; Database additionally proves the configured
        // catalog is the one that answers.
        string statement = level == SqlAlivenessLevel.Reachable
            ? "SELECT 1;"
            : "SELECT DB_NAME();";

        await executor.ScalarAsync<string>(_sqlIdentifier, statement, new Dictionary<string, object?>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        logger.LogInformation("SQL IsLive '{0}' succeeded in {1}.", _sqlIdentifier.ToString(), stopwatch.Elapsed);
        return new SqlIsLiveResult(_sqlIdentifier, level, connection, true, stopwatch.Elapsed);
    }
}
