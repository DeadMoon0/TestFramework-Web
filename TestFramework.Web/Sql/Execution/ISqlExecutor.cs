using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestFramework.Web.Sql.Execution;

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
    /// Returns a log-safe description of the connection used for an identifier.
    /// </summary>
    /// <param name="identifier">The SQL identifier.</param>
    string Describe(string identifier);
}
