using TestFramework.Core.Steps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Logging;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql.Exceptions;
using TestFramework.Web.Sql.Execution;
using TestFramework.Web.Sql.Model;

namespace TestFramework.Web.Sql.Artifacts;

/// <summary>
/// Sets up and tears down table rows.
/// </summary>
/// <typeparam name="TRow">The row model type.</typeparam>
public sealed class SqlRowArtifactDescriber<TRow> : ArtifactDescriber<SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>, SqlRowArtifactReference<TRow>>
    where TRow : class
{
    /// <summary>
    /// The canonical key mirrored from <c>TestFramework.Core.Debugger.DebugValueSchemaKeys.SqlRow</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately the same key the EF-backed SQL row artifact in TestFramework.Azure reports: both
    /// present a database row, so a consumer should draw them with one icon and one inspector rather
    /// than two that merely resemble each other. A literal rather than a reference because this
    /// package builds against the published Core; <c>SqlRowSchemaTests</c> pins it.
    /// </remarks>
    internal const string SchemaKey = "tf.artifact.sql.row";

    /// <summary>
    /// The version setup captures when it reads back what the database wrote.
    /// </summary>
    internal static readonly ArtifactVersionIdentifier SetupVersion = "setup";

    /// <summary>
    /// Identifies this artifact's debug shape, so a consumer renders it as a database row instead of
    /// falling back to a generic view keyed on the CLR type name.
    /// </summary>
    public override string DebugValueSchemaKey => SchemaKey;

    /// <inheritdoc />
    public override JToken? CreateDebugValueCustomPayload(ArtifactInstanceGeneric instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (instance.Reference is not SqlRowArtifactReference<TRow> reference)
            return null;

        return new JObject
        {
            ["connection"] = reference.SqlIdentifier.ToString(),
            ["rowType"] = typeof(TRow).Name
        };
    }

    /// <summary>
    /// Rows targeting one database are set up one at a time.
    /// </summary>
    public override ArtifactSetupParallelizationMode SetupParallelization => ArtifactSetupParallelizationMode.SerializeByArtifactType;

    /// <summary>
    /// Groups setup work by database, so unrelated databases still run concurrently.
    /// </summary>
    public override string? GetSetupParallelizationResourceKey(ArtifactInstanceGeneric artifactInstance)
    {
        ArgumentNullException.ThrowIfNull(artifactInstance);

        return artifactInstance.Reference is SqlRowArtifactReference<TRow> reference
            ? $"sql:{reference.SqlIdentifier}"
            : base.GetSetupParallelizationResourceKey(artifactInstance);
    }

    /// <summary>
    /// Ensures the row exists, updating it when it is already present.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Setup upserts rather than inserts so a rerun against a database left dirty by a previous run
    /// converges instead of failing on a duplicate key.
    /// </para>
    /// <para>
    /// A row whose key the database assigns is inserted instead - there is no key to look for yet - and the key
    /// it was given is read back and assigned to the reference through the run's store, so teardown deletes that
    /// row. A model with database-assigned columns is read back after it is written and captured as the
    /// <c>setup</c> version, so the artifact shows what the database holds rather than what the test sent.
    /// </para>
    /// </remarks>
    public override async Task Setup(RunContext context, SqlRowArtifactData<TRow> data, SqlRowArtifactReference<TRow> reference)
    {
        ArgumentNullException.ThrowIfNull(context.Services);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(context.Logger);

        SqlModelMap map = SqlConfigResolver.ResolveModelRegistry(context.Services).Resolve<TRow>();
        ISqlExecutor executor = SqlConfigResolver.ResolveExecutor(context);
        SqlRowAddress address = reference.AddressOf(map, context.Variables);

        if (address is SqlRowAddress.Given && map.GeneratedKeyColumns.Count > 0)
            throw SqlModelMapException.GeneratedKeyGiven(typeof(TRow), [.. map.GeneratedKeyColumns.Select(column => column.ColumnName)]);

        EnsureTheRowCarriesTheSuppliedKey(map, data.Row, address);

        if (reference.TryBuildKeyParameters(map, context.Variables, out IReadOnlyDictionary<string, object?>? keyParameters)
            && await ExistsAsync(context, executor, map, reference, keyParameters).ConfigureAwait(false))
        {
            await UpdateAsync(context, executor, map, data.Row, reference, keyParameters).ConfigureAwait(false);
        }
        else if (map.GeneratedKeyColumns.Count == 0)
        {
            await InsertAsync(context, executor, map, data.Row, reference).ConfigureAwait(false);
        }
        else if (!await InsertAssigningKeyAsync(context, executor, map, data.Row, reference).ConfigureAwait(false))
        {
            return;
        }

        if (map.Columns.Any(column => column.IsGenerated))
            await CaptureWhatTheDatabaseHoldsAsync(context, executor, map, reference).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the row, treating an already-deleted row as success.
    /// </summary>
    /// <remarks>
    /// A row whose key the database never assigned was never created, so there is nothing to delete - said as
    /// that, not as "already gone".
    /// </remarks>
    public override async Task Deconstruct(RunContext context, SqlRowArtifactReference<TRow> reference)
    {
        ArgumentNullException.ThrowIfNull(context.Services);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(context.Logger);

        SqlModelMap map = SqlConfigResolver.ResolveModelRegistry(context.Services).Resolve<TRow>();

        if (!reference.TryBuildKeyParameters(map, context.Variables, out IReadOnlyDictionary<string, object?>? keyParameters))
        {
            context.Logger.LogInformation("SQL row '{0}' was never created in '{1}', so there is nothing to delete.", map.ToString(), reference.SqlIdentifier.ToString());
            return;
        }

        ISqlExecutor executor = SqlConfigResolver.ResolveExecutor(context);
        SqlStatement delete = SqlStatementBuilder.DeleteByKey(map);

        int affected = await executor.ExecuteAsync(
            reference.SqlIdentifier,
            delete.Text,
            keyParameters,
            context.Deadline.Token).ConfigureAwait(false);

        context.Logger.LogInformation(
            affected > 0 ? "SQL row '{0}' deleted from '{1}'." : "SQL row '{0}' was already gone in '{1}'.",
            reference.ToString(),
            reference.SqlIdentifier.ToString());
    }

    /// <summary>
    /// Returns a readable description of the artifact kind.
    /// </summary>
    public override string ToString() => $"SQL Row<{typeof(TRow).Name}>";

    private static async Task<bool> ExistsAsync(RunContext context, ISqlExecutor executor, SqlModelMap map, SqlRowArtifactReference<TRow> reference, IReadOnlyDictionary<string, object?> keyParameters)
    {
        SqlStatement exists = SqlStatementBuilder.ExistsByKey(map);
        int found = await executor.ScalarAsync<int>(reference.SqlIdentifier, exists.Text, keyParameters, context.Deadline.Token).ConfigureAwait(false);
        return found > 0;
    }

    private static async Task UpdateAsync(RunContext context, ISqlExecutor executor, SqlModelMap map, TRow row, SqlRowArtifactReference<TRow> reference, IReadOnlyDictionary<string, object?> keyParameters)
    {
        SqlStatement update = SqlStatementBuilder.UpdateByKey(map);
        if (update.Text.Length == 0)
        {
            context.Logger!.LogInformation("SQL row '{0}' already exists and has no updatable columns.", map.ToString());
            return;
        }

        await executor.ExecuteAsync(
            reference.SqlIdentifier,
            update.Text,
            BuildUpdateParameters(map, row, keyParameters),
            context.Deadline.Token).ConfigureAwait(false);

        context.Logger!.LogInformation("SQL row '{0}' updated in '{1}'.", reference.ToString(), reference.SqlIdentifier.ToString());
    }

    private static async Task InsertAsync(RunContext context, ISqlExecutor executor, SqlModelMap map, TRow row, SqlRowArtifactReference<TRow> reference)
    {
        SqlStatement insert = SqlStatementBuilder.Insert(map);
        await executor.ExecuteAsync(
            reference.SqlIdentifier,
            insert.Text,
            BuildInsertParameters(map, row),
            context.Deadline.Token).ConfigureAwait(false);

        context.Logger!.LogInformation("SQL row '{0}' inserted into '{1}'.", reference.ToString(), reference.SqlIdentifier.ToString());
    }

    /// <summary>
    /// Inserts a row whose key the database assigns, and gives the reference that key.
    /// </summary>
    /// <returns><see langword="false"/> when the run no longer takes this attempt's writes, so the row is not
    /// this run's to clean up.</returns>
    private static async Task<bool> InsertAssigningKeyAsync(RunContext context, ISqlExecutor executor, SqlModelMap map, TRow row, SqlRowArtifactReference<TRow> reference)
    {
        SqlStatement insert = SqlStatementBuilder.InsertReturningKey(map);
        IReadOnlyList<TRow> inserted = await executor.QueryAsync<TRow>(
            reference.SqlIdentifier,
            insert.Text,
            BuildInsertParameters(map, row),
            context.Deadline.Token).ConfigureAwait(false);

        if (inserted.Count != 1)
        {
            throw new FrameworkStateException(
                $"Inserting a '{typeof(TRow).Name}' row into '{reference.SqlIdentifier}' reported {inserted.Count} key(s) instead of one, so the row the run owns cannot be told apart.")
            {
                RecoverySteps = ["An INSTEAD OF INSERT trigger on the table can swallow or multiply the insert; seed this row with a script instead and register it by its key."],
            };
        }

        IReadOnlyList<string> key = map.ReadKeyValues(inserted[0]);

        if (!context.Artifacts.Assign(reference, (held, proof) => held.AssignKey(proof, key)))
        {
            // The row exists and nothing this run holds will delete it. Said with its key, so the leftover can be found.
            context.Logger!.LogWarning(
                "SQL row '{0}' with key ({1}) was inserted into '{2}' by an attempt the run had stopped waiting for; this run does not own it and will not delete it.",
                map.ToString(),
                string.Join(", ", key),
                reference.SqlIdentifier.ToString());
            return false;
        }

        reference.PublishKeys(map, key, context.Variables);
        context.Logger!.LogInformation("SQL row '{0}' inserted into '{1}'.", reference.ToString(), reference.SqlIdentifier.ToString());
        return true;
    }

    private static async Task CaptureWhatTheDatabaseHoldsAsync(RunContext context, ISqlExecutor executor, SqlModelMap map, SqlRowArtifactReference<TRow> reference)
    {
        if (!reference.TryBuildKeyParameters(map, context.Variables, out IReadOnlyDictionary<string, object?>? keyParameters))
            return;

        SqlStatement select = SqlStatementBuilder.SelectByKey(map);
        IReadOnlyList<TRow> rows = await executor.QueryAsync<TRow>(reference.SqlIdentifier, select.Text, keyParameters, context.Deadline.Token).ConfigureAwait(false);

        // Setup does not run for an artifact the store does not hold, but a describer can be called directly, and
        // then there is no instance to version.
        ArtifactInstanceGeneric? instance = context.Artifacts.GetAll().FirstOrDefault(held => ReferenceEquals(held.Reference, reference));

        if (rows.Count == 0 || instance is null)
        {
            context.Logger!.LogWarning("SQL row '{0}' could not be read back after setup in '{1}'.", reference.ToString(), reference.SqlIdentifier.ToString());
            return;
        }

        context.Artifacts.CaptureVersion(instance, new SqlRowArtifactData<TRow>(rows[0]) { Identifier = SetupVersion });
    }

    /// <summary>
    /// Refuses seed data whose key disagrees with the reference, because teardown deletes what the reference
    /// addresses and setup writes what the row carries.
    /// </summary>
    private static void EnsureTheRowCarriesTheSuppliedKey(SqlModelMap map, TRow row, SqlRowAddress address)
    {
        foreach (SqlColumnMap column in map.KeyColumns.Where(key => !key.IsGenerated))
        {
            string addressed = address.SuppliedValueFor(map, column);
            object? carried = column.Property.GetValue(row);

            if (!Equals(map.ConvertKeyValue(column, addressed), carried))
                throw SqlModelMapException.SeedKeyDisagrees(typeof(TRow), column.ColumnName, addressed, carried is null ? null : SqlModelMap.FormatKeyValue(carried));
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildInsertParameters(SqlModelMap map, TRow row)
    {
        Dictionary<string, object?> parameters = new(StringComparer.Ordinal);
        for (int index = 0; index < map.WritableColumns.Count; index++)
            parameters[$"{SqlStatementBuilder.ValueParameterPrefix}{index}"] = map.WritableColumns[index].Property.GetValue(row);

        return parameters;
    }

    private static IReadOnlyDictionary<string, object?> BuildUpdateParameters(SqlModelMap map, TRow row, IReadOnlyDictionary<string, object?> keyParameters)
    {
        Dictionary<string, object?> parameters = new(keyParameters, StringComparer.Ordinal);
        SqlColumnMap[] assignable = [.. map.WritableColumns.Where(column => !column.IsKey)];

        for (int index = 0; index < assignable.Length; index++)
            parameters[$"{SqlStatementBuilder.ValueParameterPrefix}{index}"] = assignable[index].Property.GetValue(row);

        return parameters;
    }
}
