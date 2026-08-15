using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestFramework.Web.Sql.Execution;

/// <summary>
/// What running the batches of one script did.
/// </summary>
/// <param name="AffectedRows">The total number of affected rows reported by the batches.</param>
/// <param name="OpenTransactionCount">
/// How many transactions were still open when the batches finished, or <see langword="null"/> when
/// the executor cannot tell.
/// </param>
public readonly record struct SqlScriptExecutionResult(int AffectedRows, int? OpenTransactionCount);

/// <summary>
/// Executes statements against a configured SQL database.
/// </summary>
/// <remarks>
/// This is the seam that keeps the rest of the SQL surface free of any particular data access
/// library, and the place a different provider would plug in.
/// </remarks>
public interface ISqlExecutor
{
    /// <summary>
    /// Executes a statement and returns the number of affected rows.
    /// </summary>
    /// <param name="identifier">The SQL identifier to run against.</param>
    /// <param name="statement">The statement text.</param>
    /// <param name="parameters">The parameter values, keyed by name.</param>
    /// <param name="cancellationToken">The cancellation token for the running step.</param>
    Task<int> ExecuteAsync(string identifier, string statement, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken);

    /// <summary>
    /// Executes a statement and returns its first column of the first row.
    /// </summary>
    /// <typeparam name="TValue">The scalar value type.</typeparam>
    /// <param name="identifier">The SQL identifier to run against.</param>
    /// <param name="statement">The statement text.</param>
    /// <param name="parameters">The parameter values, keyed by name.</param>
    /// <param name="cancellationToken">The cancellation token for the running step.</param>
    Task<TValue?> ScalarAsync<TValue>(string identifier, string statement, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken);

    /// <summary>
    /// Executes a query and materializes its rows.
    /// </summary>
    /// <typeparam name="TRow">The row model type.</typeparam>
    /// <param name="identifier">The SQL identifier to run against.</param>
    /// <param name="statement">The statement text.</param>
    /// <param name="parameters">The parameter values, keyed by name.</param>
    /// <param name="cancellationToken">The cancellation token for the running step.</param>
    Task<IReadOnlyList<TRow>> QueryAsync<TRow>(string identifier, string statement, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the batches of one script.
    /// </summary>
    /// <param name="identifier">The SQL identifier to run against.</param>
    /// <param name="batches">The <c>GO</c>-separated batches, in order.</param>
    /// <param name="parameters">The parameter values shared by every batch, keyed by name.</param>
    /// <param name="cancellationToken">The cancellation token for the running step.</param>
    /// <remarks>
    /// <para>
    /// The batches of a script are one unit of work and belong on ONE connection. A <c>#temp</c>
    /// table, a <c>SET</c> option, <c>SCOPE_IDENTITY()</c> and a transaction spanning a <c>GO</c>
    /// are all connection state: run each batch on its own connection and they silently vanish
    /// between batches — pooled reuse does not save it, because the pool issues
    /// <c>sp_reset_connection</c> by design.
    /// </para>
    /// <para>
    /// This is a default interface method purely so that adding it does not break the implementers
    /// of this public seam. Its default body is the old per-batch behaviour, statement by statement
    /// through <see cref="ExecuteAsync"/>: an existing external executor therefore keeps the old
    /// semantics until it overrides this method.
    /// </para>
    /// </remarks>
    async Task<SqlScriptExecutionResult> ExecuteScriptAsync(
        string identifier,
        IReadOnlyList<string> batches,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batches);

        int affected = 0;
        foreach (string batch in batches)
            affected += await ExecuteAsync(identifier, batch, parameters, cancellationToken).ConfigureAwait(false);

        // A per-batch executor has no connection to inspect, so it cannot report a dangling transaction.
        return new SqlScriptExecutionResult(affected, null);
    }

    /// <summary>
    /// Returns a log-safe description of the connection used for an identifier.
    /// </summary>
    /// <param name="identifier">The SQL identifier.</param>
    string Describe(string identifier);
}
