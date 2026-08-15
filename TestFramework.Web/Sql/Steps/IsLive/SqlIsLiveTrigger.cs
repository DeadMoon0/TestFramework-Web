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
    /// The connection opens and the server answers a trivial query.
    /// </summary>
    /// <remarks>
    /// Not "the server is up but the database may not be". Configured from parts, the connection
    /// string always names a catalog — <c>SqlConnectionStringFactory</c> requires <c>Database</c>
    /// and puts it in <c>InitialCatalog</c> — so opening the connection has already opened the
    /// configured database, and this level differs from <see cref="Database"/> only in the query it
    /// then sends: <c>SELECT 1</c> against <c>SELECT DB_NAME()</c>. A verbatim
    /// <c>ConnectionString</c> that omits <c>Initial Catalog</c> is the only case where the two
    /// really differ, and there the login's default database is what answered.
    /// </remarks>
    Reachable = 0,

    /// <summary>
    /// The configured database answers with its own name.
    /// </summary>
    Database = 1,
}

/// <summary>
/// Result of a SQL liveness probe.
/// </summary>
/// <remarks>
/// A result only exists when the probe succeeded: a failing probe throws
/// <see cref="Exceptions.SqlExecutionFailedException"/> or
/// <see cref="Exceptions.SqlConfigurationValidationException"/> instead of returning.
/// </remarks>
/// <param name="SqlIdentifier">The database that was probed.</param>
/// <param name="AlivenessLevel">The requested probe depth.</param>
/// <param name="Connection">A log-safe description of the connection.</param>
/// <param name="Success">
/// Always <see langword="true"/>. A failed probe throws rather than returning a result, so there is
/// no value of this record in which it is false. It exists so that an asserted probe reads like any
/// other step result instead of forcing a different assertion shape on the one step that cannot
/// fail quietly.
/// </param>
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
