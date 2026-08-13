using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Logging;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql.Execution;
using TestFramework.Web.Sql.Model;

namespace TestFramework.Web.Sql.Artifacts;

/// <summary>
/// Finds table rows with a where clause and variable-backed parameters.
/// </summary>
/// <typeparam name="TRow">The row model type.</typeparam>
/// <remarks>
/// Rows located this way are observed, not owned: the framework never deletes them during teardown.
/// </remarks>
public sealed class SqlRowWhereFinder<TRow> : ArtifactFinder<SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>, SqlRowArtifactReference<TRow>>
    where TRow : class
{
    private readonly SqlIdentifier _sqlIdentifier;
    private readonly string _whereClause;
    private readonly SqlParameterSet _parameters = new();

    /// <summary>
    /// Creates a finder for a where clause.
    /// </summary>
    /// <param name="sqlIdentifier">The SQL identifier to query.</param>
    /// <param name="whereClause">The predicate, without the <c>WHERE</c> keyword.</param>
    public SqlRowWhereFinder(SqlIdentifier sqlIdentifier, string whereClause)
    {
        ArgumentNullException.ThrowIfNull(sqlIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(whereClause);

        _sqlIdentifier = sqlIdentifier;
        _whereClause = whereClause;
    }

    /// <summary>
    /// Binds a parameter used by the where clause.
    /// </summary>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The variable carrying the value.</param>
    public SqlRowWhereFinder<TRow> WithParameter<TValue>(string name, VariableReference<TValue> value)
    {
        _parameters.Add(name, value);
        return this;
    }

    /// <summary>
    /// Finds the first matching row.
    /// </summary>
    public override async Task<ArtifactFinderResult?> FindAsync(IServiceProvider serviceProvider, VariableStore variableStore, ScopedLogger logger, CancellationToken cancellationToken)
    {
        IReadOnlyList<SqlRowArtifactReference<TRow>> references = await FindReferencesAsync(serviceProvider, variableStore, logger, cancellationToken).ConfigureAwait(false);
        return references.Count == 0 ? null : new ArtifactFinderResult(references[0]);
    }

    /// <summary>
    /// Finds every matching row.
    /// </summary>
    public override async Task<ArtifactFinderResultMulti> FindMultiAsync(IServiceProvider serviceProvider, VariableStore variableStore, ScopedLogger logger, CancellationToken cancellationToken)
    {
        IReadOnlyList<SqlRowArtifactReference<TRow>> references = await FindReferencesAsync(serviceProvider, variableStore, logger, cancellationToken).ConfigureAwait(false);
        return new ArtifactFinderResultMulti([.. references.Select(reference => new ArtifactFinderResult(reference))]);
    }

    private async Task<IReadOnlyList<SqlRowArtifactReference<TRow>>> FindReferencesAsync(IServiceProvider serviceProvider, VariableStore variableStore, ScopedLogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        SqlModelMap map = SqlConfigResolver.ResolveModelRegistry(serviceProvider).Resolve<TRow>();
        ISqlExecutor executor = SqlConfigResolver.ResolveExecutor(serviceProvider);
        SqlStatement statement = SqlStatementBuilder.SelectWhere(map, _whereClause, _parameters.Names);

        IReadOnlyList<TRow> rows = await executor.QueryAsync<TRow>(
            _sqlIdentifier,
            statement.Text,
            _parameters.Resolve(variableStore),
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("SQL find '{0}' matched {1} row(s) in '{2}'.", map.ToString(), rows.Count, _sqlIdentifier.ToString());

        return [.. rows.Select(row => SqlRowArtifactReference<TRow>.Observed(_sqlIdentifier, ReadKeyValues(map, row)))];
    }

    private static IReadOnlyList<string> ReadKeyValues(SqlModelMap map, TRow row)
        => [.. map.KeyColumns.Select(column =>
            Convert.ToString(column.Property.GetValue(row), CultureInfo.InvariantCulture)
            ?? throw new TestFramework.Core.Exceptions.FrameworkStateException(
                $"The key column '{column.ColumnName}' of '{typeof(TRow).Name}' resolved to null, so the row cannot be addressed."))];
}
