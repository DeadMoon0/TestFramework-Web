using TestFramework.Core.Steps;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql.Exceptions;
using TestFramework.Web.Sql.Execution;
using TestFramework.Web.Sql.Model;

namespace TestFramework.Web.Sql.Artifacts;

/// <summary>
/// Addresses one table row by its key values.
/// </summary>
/// <typeparam name="TRow">The row model type.</typeparam>
/// <remarks>
/// <para>
/// Give every key value to address a row that exists, or that setup writes under that key. For a model whose
/// key the database assigns - an identity, a <c>NEWSEQUENTIALID()</c> default - leave those key columns out:
/// setup inserts the row, reads the assigned key back and addresses that row from then on, and
/// <see cref="KeyTo{TKey}"/> hands the key to later steps.
/// </para>
/// <para>
/// The reference can always address its row, so teardown deletes it - whether the test created the row
/// or found it. Chain <c>MarkReadonly()</c> onto the declaring call for a row the run only reads.
/// </para>
/// </remarks>
public sealed class SqlRowArtifactReference<TRow> : ArtifactReference<SqlRowArtifactReference<TRow>, SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>>, ISqlArtifactReference
    where TRow : class
{
    private readonly VariableReference<string>[] _keyValues;
    private KeyPublication[] _publications = [];
    private SqlRowAddress? _address;

    /// <summary>
    /// Creates a reference to a row the test owns.
    /// </summary>
    /// <param name="sqlIdentifier">The SQL identifier.</param>
    /// <param name="keyValues">The key values, in the key order declared by the model map - all of them, or for a
    /// model whose key the database assigns, only the ones the test supplies.</param>
    public SqlRowArtifactReference(SqlIdentifier sqlIdentifier, params VariableReference<string>[] keyValues)
    {
        ArgumentNullException.ThrowIfNull(sqlIdentifier);
        ArgumentNullException.ThrowIfNull(keyValues);

        SqlIdentifier = sqlIdentifier;
        _keyValues = keyValues;

        // Pinning refuses a set of key values that addresses no row, and setup assigns the rest, so the reference
        // can always address the row it deletes. Whether it may delete it is the timeline's call, through
        // MarkReadonly().
        CanDeconstruct = true;
    }

    /// <inheritdoc />
    public SqlIdentifier SqlIdentifier { get; }

    /// <summary>
    /// Creates a reference to a row a finder located, carrying the key values it already resolved.
    /// </summary>
    /// <param name="sqlIdentifier">The SQL identifier.</param>
    /// <param name="keyValues">The already-resolved key values.</param>
    internal static SqlRowArtifactReference<TRow> Discovered(SqlIdentifier sqlIdentifier, IReadOnlyList<string> keyValues)
        => new(sqlIdentifier, [.. keyValues.Select(Var.Const)])
        {
            _address = new SqlRowAddress.Given([.. keyValues]),
        };

    /// <summary>
    /// The key values of the row this reference addresses, in key order; empty until they are known.
    /// </summary>
    public IReadOnlyList<string> KeyValues => _address switch
    {
        SqlRowAddress.Given given => given.Values,
        SqlRowAddress.Assigned assigned => assigned.Values,
        _ => [],
    };

    /// <summary>
    /// Writes a key value of the row to a variable, for the steps that come after it.
    /// </summary>
    /// <remarks>
    /// Written once the key is known: when the reference is pinned for a key the test gave, and when setup has
    /// read back a key the database assigned. The variable is declared as this artifact's output, so a later step
    /// reading it is checked when the run is planned.
    /// </remarks>
    /// <typeparam name="TKey">The key property's type, which the variable takes.</typeparam>
    /// <param name="key">The key property, such as <c>x =&gt; x.Id</c>.</param>
    /// <param name="into">The variable to write.</param>
    /// <returns>A reference that also publishes that key.</returns>
    public SqlRowArtifactReference<TRow> KeyTo<TKey>(Expression<Func<TRow, TKey>> key, VariableIdentifier into)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(into);

        PropertyInfo property = key.Body is MemberExpression { Member: PropertyInfo member }
            ? member
            : throw SqlModelMapException.NotAProperty(typeof(TRow), key.ToString());

        if (_publications.Any(publication => string.Equals(publication.Into.Identifier, into.Identifier, StringComparison.Ordinal)))
            throw new ArgumentException($"'{into.Identifier}' already receives a key of this row.", nameof(into));

        return new SqlRowArtifactReference<TRow>(SqlIdentifier, _keyValues)
        {
            _publications = [.. _publications, KeyPublication.For<TKey>(property, into)],
            _address = _address,
        };
    }

    /// <summary>
    /// Resolves the key variables once, so teardown still addresses the same row.
    /// </summary>
    /// <remarks>
    /// A reference a finder produced already carries resolved values, so pinning it again would
    /// re-resolve variables that only exist to describe what was found.
    /// </remarks>
    public override void OnPinReference(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_address is not null)
            return;

        ArgumentNullException.ThrowIfNull(context.Services);

        SqlModelMap map = SqlConfigResolver.ResolveModelRegistry(context.Services).Resolve<TRow>();
        EnsurePublishedKeysAreKeys(map);

        _address = AddressFrom(map, context.Variables);

        if (_address is SqlRowAddress.Given given)
            PublishKeys(map, given.Values, context.Variables);
    }

    /// <summary>
    /// Declares the key variables as inputs, and the variables <see cref="KeyTo{TKey}"/> writes as outputs, of the
    /// steps that use this reference.
    /// </summary>
    public override void DeclareIO(StepIOContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        foreach (VariableReference<string> keyValue in _keyValues)
        {
            if (keyValue.Identifier is { } identifier)
                contract.Input(identifier, typeof(string));
        }

        foreach (KeyPublication publication in _publications)
            contract.Output(publication.Into, publication.Type);
    }

    /// <summary>
    /// Reads the current state of the row.
    /// </summary>
    /// <remarks>
    /// A row whose key the database has not assigned yet has not been created, so it is not found.
    /// </remarks>
    public override async Task<ArtifactResolveResult<SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>, SqlRowArtifactReference<TRow>>> ResolveToDataAsync(RunContext context, ArtifactVersionIdentifier versionIdentifier)
    {
        ArgumentNullException.ThrowIfNull(context.Services);

        SqlModelMap map = SqlConfigResolver.ResolveModelRegistry(context.Services).Resolve<TRow>();

        if (!TryBuildKeyParameters(map, context.Variables, out IReadOnlyDictionary<string, object?>? keyParameters))
            return new ArtifactResolveResult<SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>, SqlRowArtifactReference<TRow>> { Found = false };

        ISqlExecutor executor = SqlConfigResolver.ResolveExecutor(context);
        SqlStatement statement = SqlStatementBuilder.SelectByKey(map);

        IReadOnlyList<TRow> rows = await executor.QueryAsync<TRow>(
            SqlIdentifier,
            statement.Text,
            keyParameters,
            context.Deadline.Token).ConfigureAwait(false);

        return rows.Count == 0
            ? new ArtifactResolveResult<SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>, SqlRowArtifactReference<TRow>> { Found = false }
            : new ArtifactResolveResult<SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>, SqlRowArtifactReference<TRow>>
            {
                Found = true,
                Data = new SqlRowArtifactData<TRow>(rows[0]) { Identifier = versionIdentifier },
            };
    }

    /// <summary>
    /// Which row this reference addresses: the pinned answer, or the one its variables give now.
    /// </summary>
    /// <param name="map">The model map.</param>
    /// <param name="variableStore">The variables the key values resolve against when the reference is not pinned.</param>
    /// <returns>The address.</returns>
    internal SqlRowAddress AddressOf(SqlModelMap map, VariableStore variableStore) => _address ?? AddressFrom(map, variableStore);

    /// <summary>
    /// Builds the key parameters for a statement produced by <see cref="SqlStatementBuilder"/>.
    /// </summary>
    /// <param name="map">The model map.</param>
    /// <param name="variableStore">The variables the key values resolve against.</param>
    /// <param name="parameters">The key parameters, when the row is addressable.</param>
    /// <returns><see langword="false"/> while the database has not assigned the row's key, so there is no row yet.</returns>
    internal bool TryBuildKeyParameters(SqlModelMap map, VariableStore variableStore, [NotNullWhen(true)] out IReadOnlyDictionary<string, object?>? parameters)
    {
        ArgumentNullException.ThrowIfNull(map);

        IReadOnlyList<string>? values = AddressOf(map, variableStore) switch
        {
            SqlRowAddress.Given given => given.Values,
            SqlRowAddress.Assigned assigned => assigned.Values,
            _ => null,
        };

        if (values is null)
        {
            parameters = null;
            return false;
        }

        Dictionary<string, object?> keyParameters = new(StringComparer.Ordinal);
        for (int index = 0; index < values.Count; index++)
            keyParameters[$"{SqlStatementBuilder.KeyParameterPrefix}{index}"] = map.ConvertKeyValue(map.KeyColumns[index], values[index]);

        parameters = keyParameters;
        return true;
    }

    /// <summary>
    /// Records the key the database assigned to the row setup created.
    /// </summary>
    /// <param name="proof">The store's proof that the run admitted it.</param>
    /// <param name="keyValues">The key values the database reported, in key order.</param>
    internal void AssignKey(ArtifactAssignment proof, IReadOnlyList<string> keyValues)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(keyValues);
        proof.EnsureOpen();

        // A key the test gave is the test's; only a row setup created takes a key from the database.
        if (_address is SqlRowAddress.Given)
            throw new InvalidOperationException($"'{this}' addresses a row by the key the test gave, so it takes no assigned key.");

        _address = new SqlRowAddress.Assigned([.. keyValues]);
    }

    /// <summary>
    /// Writes the key values <see cref="KeyTo{TKey}"/> asked for.
    /// </summary>
    /// <param name="map">The model map.</param>
    /// <param name="keyValues">The row's key values, in key order.</param>
    /// <param name="variableStore">The variables to write.</param>
    internal void PublishKeys(SqlModelMap map, IReadOnlyList<string> keyValues, VariableStore variableStore)
    {
        foreach (KeyPublication publication in _publications)
        {
            int index = IndexOfKey(map, publication.Property);
            publication.Write(variableStore, map.ConvertKeyValue(map.KeyColumns[index], keyValues[index]));
        }
    }

    /// <summary>
    /// Returns a readable description of the reference.
    /// </summary>
    public override string ToString()
    {
        string key = _address switch
        {
            SqlRowAddress.Given given => string.Join(", ", given.Values),
            SqlRowAddress.Assigned assigned => string.Join(", ", assigned.Values),
            SqlRowAddress.AwaitingGeneratedKey => "<assigned on setup>",
            _ => "<unpinned>",
        };

        return $"SQL row {typeof(TRow).Name}({key}) in '{SqlIdentifier}'";
    }

    private SqlRowAddress AddressFrom(SqlModelMap map, VariableStore variableStore)
    {
        string[] supplied = [.. _keyValues.Select((value, index) => value.GetRequiredValue(variableStore, $"key value {index}"))];

        if (supplied.Length == map.KeyColumns.Count)
            return new SqlRowAddress.Given(supplied);

        if (map.GeneratedKeyColumns.Count > 0 && supplied.Length == map.KeyColumns.Count - map.GeneratedKeyColumns.Count)
            return new SqlRowAddress.AwaitingGeneratedKey(supplied);

        throw SqlModelMapException.KeyValueCount(
            typeof(TRow),
            supplied.Length,
            [.. map.KeyColumns.Select(column => column.ColumnName)],
            [.. map.GeneratedKeyColumns.Select(column => column.ColumnName)]);
    }

    private void EnsurePublishedKeysAreKeys(SqlModelMap map)
    {
        foreach (KeyPublication publication in _publications)
        {
            if (IndexOfKey(map, publication.Property) < 0)
                throw SqlModelMapException.NotAKey(typeof(TRow), publication.Property.Name, [.. map.KeyColumns.Select(column => column.Property.Name)]);
        }
    }

    private static int IndexOfKey(SqlModelMap map, PropertyInfo property)
    {
        for (int index = 0; index < map.KeyColumns.Count; index++)
        {
            if (string.Equals(map.KeyColumns[index].Property.Name, property.Name, StringComparison.Ordinal))
                return index;
        }

        return -1;
    }

    /// <summary>
    /// One <see cref="KeyTo{TKey}"/>: which key goes to which variable, typed when it was declared.
    /// </summary>
    private sealed class KeyPublication
    {
        private readonly Action<VariableStore, object> write;

        private KeyPublication(PropertyInfo property, VariableIdentifier into, Type type, Action<VariableStore, object> write)
        {
            Property = property;
            Into = into;
            Type = type;
            this.write = write;
        }

        public PropertyInfo Property { get; }

        public VariableIdentifier Into { get; }

        public Type Type { get; }

        public static KeyPublication For<TKey>(PropertyInfo property, VariableIdentifier into)
            => new(property, into, typeof(TKey), (store, value) => store.SetVariable(into, (TKey)value));

        public void Write(VariableStore store, object value) => write(store, value);
    }
}
