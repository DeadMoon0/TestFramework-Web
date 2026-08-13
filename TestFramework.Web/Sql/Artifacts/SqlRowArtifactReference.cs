using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql.Execution;
using TestFramework.Web.Sql.Model;

namespace TestFramework.Web.Sql.Artifacts;

/// <summary>
/// Addresses one table row by its key values.
/// </summary>
/// <typeparam name="TRow">The row model type.</typeparam>
/// <remarks>
/// A reference the test creates owns its row and deletes it during teardown. A reference produced by
/// a finder does not: a test must never delete data the application under test created.
/// </remarks>
public sealed class SqlRowArtifactReference<TRow> : ArtifactReference<SqlRowArtifactReference<TRow>, SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>>, ISqlArtifactReference
    where TRow : class
{
    private readonly VariableReference<string>[] _keyValues;
    private string[] _pinnedKeyValues = [];

    /// <summary>
    /// Creates a reference to a row the test owns.
    /// </summary>
    /// <param name="sqlIdentifier">The SQL identifier.</param>
    /// <param name="keyValues">The key values, in the key order declared by the model map.</param>
    public SqlRowArtifactReference(SqlIdentifier sqlIdentifier, params VariableReference<string>[] keyValues)
        : this(sqlIdentifier, ownsRow: true, keyValues)
    {
    }

    private SqlRowArtifactReference(SqlIdentifier sqlIdentifier, bool ownsRow, params VariableReference<string>[] keyValues)
    {
        ArgumentNullException.ThrowIfNull(sqlIdentifier);
        ArgumentNullException.ThrowIfNull(keyValues);

        if (keyValues.Length == 0)
            throw new ArgumentException("At least one key value must be provided.", nameof(keyValues));

        SqlIdentifier = sqlIdentifier;
        _keyValues = keyValues;
        CanDeconstruct = ownsRow;
    }

    /// <inheritdoc />
    public SqlIdentifier SqlIdentifier { get; }

    /// <summary>
    /// Creates a reference to a row the test observed but does not own.
    /// </summary>
    /// <param name="sqlIdentifier">The SQL identifier.</param>
    /// <param name="keyValues">The already-resolved key values.</param>
    internal static SqlRowArtifactReference<TRow> Observed(SqlIdentifier sqlIdentifier, IReadOnlyList<string> keyValues)
        => new(sqlIdentifier, ownsRow: false, [.. keyValues.Select(Var.Const)])
        {
            _pinnedKeyValues = [.. keyValues],
        };

    /// <summary>
    /// The key values resolved for this reference.
    /// </summary>
    public IReadOnlyList<string> KeyValues => _pinnedKeyValues;

    /// <summary>
    /// Resolves the key variables once, so teardown still addresses the same row.
    /// </summary>
    /// <remarks>
    /// A reference a finder produced already carries resolved values, so pinning it again would
    /// re-resolve variables that only exist to describe what was found.
    /// </remarks>
    public override void OnPinReference(VariableStore variableStore, ScopedLogger logger)
    {
        if (_pinnedKeyValues.Length > 0)
            return;

        _pinnedKeyValues = [.. _keyValues.Select((value, index) => value.GetRequiredValue(variableStore, $"key value {index}"))];
    }

    /// <summary>
    /// Declares the key variables as inputs of the steps that use this reference.
    /// </summary>
    public override void DeclareIO(StepIOContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        foreach (VariableReference<string> keyValue in _keyValues)
        {
            if (keyValue.Identifier is { } identifier)
                contract.Inputs.Add(new StepIOEntry(identifier.Identifier, StepIOKind.Variable, true, typeof(string)));
        }
    }

    /// <summary>
    /// Reads the current state of the row.
    /// </summary>
    public override async Task<ArtifactResolveResult<SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>, SqlRowArtifactReference<TRow>>> ResolveToDataAsync(
        IServiceProvider serviceProvider,
        ArtifactVersionIdentifier versionIdentifier,
        VariableStore variableStore,
        ScopedLogger logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        SqlModelMap map = SqlConfigResolver.ResolveModelRegistry(serviceProvider).Resolve<TRow>();
        ISqlExecutor executor = SqlConfigResolver.ResolveExecutor(serviceProvider);
        SqlStatement statement = SqlStatementBuilder.SelectByKey(map);

        IReadOnlyList<TRow> rows = await executor.QueryAsync<TRow>(
            SqlIdentifier,
            statement.Text,
            BuildKeyParameters(map, variableStore),
            System.Threading.CancellationToken.None).ConfigureAwait(false);

        return rows.Count == 0
            ? new ArtifactResolveResult<SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>, SqlRowArtifactReference<TRow>> { Found = false }
            : new ArtifactResolveResult<SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>, SqlRowArtifactReference<TRow>>
            {
                Found = true,
                Data = new SqlRowArtifactData<TRow>(rows[0]) { Identifier = versionIdentifier },
            };
    }

    /// <summary>
    /// Builds the key parameters for a statement produced by <see cref="SqlStatementBuilder"/>.
    /// </summary>
    /// <param name="map">The model map.</param>
    /// <param name="variableStore">The variable store, used when the reference was not pinned yet.</param>
    internal IReadOnlyDictionary<string, object?> BuildKeyParameters(SqlModelMap map, VariableStore variableStore)
    {
        ArgumentNullException.ThrowIfNull(map);

        string[] values = _pinnedKeyValues.Length > 0
            ? _pinnedKeyValues
            : [.. _keyValues.Select((value, index) => value.GetRequiredValue(variableStore, $"key value {index}"))];

        if (values.Length != map.KeyColumns.Count)
        {
            throw new TestFramework.Core.Exceptions.FrameworkConfigurationException(
                $"'{typeof(TRow).Name}' maps to {map.KeyColumns.Count} key column(s) but {values.Length} key value(s) were supplied. "
                + $"Key order is {string.Join(", ", map.KeyColumns.Select(column => column.ColumnName))}.");
        }

        Dictionary<string, object?> parameters = new(StringComparer.Ordinal);
        for (int index = 0; index < values.Length; index++)
            parameters[$"{SqlStatementBuilder.KeyParameterPrefix}{index}"] = map.ConvertKeyValue(map.KeyColumns[index], values[index]);

        return parameters;
    }


    /// <summary>
    /// Returns a readable description of the reference.
    /// </summary>
    public override string ToString()
        => $"SQL row {typeof(TRow).Name}({string.Join(", ", _pinnedKeyValues.Length > 0 ? _pinnedKeyValues : ["<unpinned>"])}) in '{SqlIdentifier}'";
}
