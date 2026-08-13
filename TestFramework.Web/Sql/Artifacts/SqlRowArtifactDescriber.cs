using System;
using System.Collections.Generic;
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
/// Sets up and tears down table rows.
/// </summary>
/// <typeparam name="TRow">The row model type.</typeparam>
public sealed class SqlRowArtifactDescriber<TRow> : ArtifactDescriber<SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>, SqlRowArtifactReference<TRow>>
    where TRow : class
{
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
    /// Setup upserts rather than inserts so a rerun against a database left dirty by a previous run
    /// converges instead of failing on a duplicate key.
    /// </remarks>
    public override async Task Setup(
        IServiceProvider serviceProvider,
        SqlRowArtifactData<TRow> data,
        SqlRowArtifactReference<TRow> reference,
        VariableStore variableStore,
        ScopedLogger logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(logger);

        SqlModelMap map = SqlConfigResolver.ResolveModelRegistry(serviceProvider).Resolve<TRow>();
        ISqlExecutor executor = SqlConfigResolver.ResolveExecutor(serviceProvider);
        IReadOnlyDictionary<string, object?> keyParameters = reference.BuildKeyParameters(map, variableStore);

        SqlStatement exists = SqlStatementBuilder.ExistsByKey(map);
        int found = await executor.ScalarAsync<int>(reference.SqlIdentifier, exists.Text, keyParameters, CancellationToken.None).ConfigureAwait(false);

        if (found > 0)
        {
            SqlStatement update = SqlStatementBuilder.UpdateByKey(map);
            if (update.Text.Length == 0)
            {
                logger.LogInformation("SQL row '{0}' already exists and has no updatable columns.", map.ToString());
                return;
            }

            await executor.ExecuteAsync(
                reference.SqlIdentifier,
                update.Text,
                BuildUpdateParameters(map, data.Row, keyParameters),
                CancellationToken.None).ConfigureAwait(false);

            logger.LogInformation("SQL row '{0}' updated in '{1}'.", map.ToString(), reference.SqlIdentifier.ToString());
            return;
        }

        SqlStatement insert = SqlStatementBuilder.Insert(map);
        await executor.ExecuteAsync(
            reference.SqlIdentifier,
            insert.Text,
            BuildInsertParameters(map, data.Row),
            CancellationToken.None).ConfigureAwait(false);

        logger.LogInformation("SQL row '{0}' inserted into '{1}'.", map.ToString(), reference.SqlIdentifier.ToString());
    }

    /// <summary>
    /// Removes the row, treating an already-deleted row as success.
    /// </summary>
    public override async Task Deconstruct(
        IServiceProvider serviceProvider,
        SqlRowArtifactReference<TRow> reference,
        VariableStore variableStore,
        ScopedLogger logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(logger);

        SqlModelMap map = SqlConfigResolver.ResolveModelRegistry(serviceProvider).Resolve<TRow>();
        ISqlExecutor executor = SqlConfigResolver.ResolveExecutor(serviceProvider);
        SqlStatement delete = SqlStatementBuilder.DeleteByKey(map);

        int affected = await executor.ExecuteAsync(
            reference.SqlIdentifier,
            delete.Text,
            reference.BuildKeyParameters(map, variableStore),
            CancellationToken.None).ConfigureAwait(false);

        logger.LogInformation(
            affected > 0 ? "SQL row '{0}' deleted from '{1}'." : "SQL row '{0}' was already gone in '{1}'.",
            map.ToString(),
            reference.SqlIdentifier.ToString());
    }

    /// <summary>
    /// Returns a readable description of the artifact kind.
    /// </summary>
    public override string ToString() => $"SQL Row<{typeof(TRow).Name}>";

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
