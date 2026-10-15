using TestFramework.Core.Steps;
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
using System.ComponentModel;

namespace TestFramework.Web.Sql.Artifacts;

/// <summary>
/// Finds table rows with a where clause and variable-backed parameters.
/// </summary>
/// <typeparam name="TRow">The row model type.</typeparam>
/// <remarks>
/// Rows located this way are held by the run like any other: teardown deletes them, whether the test created them
/// or found them. Chain <c>.MarkReadonly()</c> on the step to only look.
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
    /// Binds a parameter to a value.
    /// </summary>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The value.</param>
    /// <returns>The same instance, for chaining.</returns>
    public SqlRowWhereFinder<TRow> WithParameter<TValue>(string name, TValue value)
        => WithParameterFrom(name, Var.Const(NotAValue.Require(value, $"WithParameter('{name}')", $"WithParameterFrom('{name}', ...)")));

    /// <summary>
    /// Binds a parameter to a variable, read when the step runs.
    /// </summary>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The variable carrying the value.</param>
    /// <returns>The same instance, for chaining.</returns>
    public SqlRowWhereFinder<TRow> WithParameterFrom<TValue>(string name, VariableReference<TValue> value)
{
        _parameters.Add(name, value);
        return this;
    }

    /// <summary>Binds a parameter to a constant, which is a value.</summary>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The constant.</param>
    /// <returns>The same instance, for chaining.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SqlRowWhereFinder<TRow> WithParameter<TValue>(string name, ConstVariable<TValue> value) => WithParameterFrom(name, value);

    /// <summary>Refused: a variable is not a value. Use the <c>…From</c> twin.</summary>
    [Obsolete(NotAValue.Message, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SqlRowWhereFinder<TRow> WithParameter<TValue>(string name, VariableReference<TValue> value) => throw NotAValue.For($"WithParameter('{name}')", $"WithParameterFrom('{name}', ...)");

    /// <summary>Refused: a variable is not a value. Use the <c>…From</c> twin.</summary>
    [Obsolete(NotAValue.Message, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SqlRowWhereFinder<TRow> WithParameter<TValue>(string name, ResolvableVariable<TValue> value) => throw NotAValue.For($"WithParameter('{name}')", $"WithParameterFrom('{name}', ...)");

    /// <summary>Refused: a variable is not a value. Use the <c>…From</c> twin.</summary>
    [Obsolete(NotAValue.Message, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SqlRowWhereFinder<TRow> WithParameter<TVar, TValue>(string name, ImmutableVariable<TVar, TValue> value)
        where TVar : VariableReference<TValue> => throw NotAValue.For($"WithParameter('{name}')", $"WithParameterFrom('{name}', ...)");


    /// <summary>
    /// Finds the first matching row.
    /// </summary>
    public override async Task<ArtifactFinderResult?> FindAsync(RunContext context)
    {
        IReadOnlyList<SqlRowArtifactReference<TRow>> references = await FindReferencesAsync(context).ConfigureAwait(false);
        return references.Count == 0 ? null : new ArtifactFinderResult(references[0]);
    }

    /// <summary>
    /// Finds every matching row.
    /// </summary>
    public override async Task<ArtifactFinderResultMulti> FindMultiAsync(RunContext context)
    {
        IReadOnlyList<SqlRowArtifactReference<TRow>> references = await FindReferencesAsync(context).ConfigureAwait(false);
        return new ArtifactFinderResultMulti([.. references.Select(reference => new ArtifactFinderResult(reference))]);
    }

    /// <remarks>
    /// The run rather than four pieces of one. It needed the run anyway - a database this run started has a
    /// connection string that exists nowhere else - and taking the pieces is how a caller ends up with no
    /// deadline to read; see §7 of the guardrails.
    /// </remarks>
    private async Task<IReadOnlyList<SqlRowArtifactReference<TRow>>> FindReferencesAsync(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        VariableStore variableStore = context.Variables;
        ScopedLogger logger = context.Logger;
        CancellationToken cancellationToken = context.Deadline.Token;

        SqlModelMap map = SqlConfigResolver.ResolveModelRegistry(context.Services).Resolve<TRow>();
        ISqlExecutor executor = SqlConfigResolver.ResolveExecutor(context);
        SqlStatement statement = SqlStatementBuilder.SelectWhere(map, _whereClause, _parameters.Names);

        IReadOnlyList<TRow> rows = await executor.QueryAsync<TRow>(
            _sqlIdentifier,
            statement.Text,
            _parameters.Resolve(variableStore),
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation("SQL find '{0}' matched {1} row(s) in '{2}'.", map.ToString(), rows.Count, _sqlIdentifier.ToString());

        return [.. rows.Select(row => SqlRowArtifactReference<TRow>.Discovered(_sqlIdentifier, map.ReadKeyValues(row)))];
    }
}
