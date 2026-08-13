using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web.Sql.Exceptions;

/// <summary>
/// Thrown when a SQL statement fails to execute.
/// </summary>
public sealed class SqlExecutionFailedException : TimelineFrameworkException
{
    /// <summary>
    /// Creates an exception describing a failed statement.
    /// </summary>
    /// <param name="identifier">The SQL identifier that was used.</param>
    /// <param name="connectionDescription">A log-safe description of the connection.</param>
    /// <param name="statement">The statement text that failed.</param>
    /// <param name="parameterNames">The parameter names bound to the statement. Values are never included.</param>
    /// <param name="elapsed">How long the attempt took before failing.</param>
    /// <param name="innerException">The underlying provider exception.</param>
    /// <returns>The exception describing the failure.</returns>
    public static SqlExecutionFailedException Statement(
        string identifier,
        string connectionDescription,
        string statement,
        IEnumerable<string> parameterNames,
        TimeSpan elapsed,
        Exception innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);

        string[] names = [.. parameterNames];
        List<string> details =
        [
            $"SQL '{identifier}' failed after {elapsed:g} against {connectionDescription}: {innerException.GetType().Name}: {innerException.Message}",
            $"Statement: {Summarize(statement)}",
        ];

        if (names.Length > 0)
            details.Add($"Parameters: {string.Join(", ", names)}");

        return new SqlExecutionFailedException(
            string.Join(Environment.NewLine, details),
            [
                "Check the statement against the actual schema; the table or column names come from the model map.",
                $"Verify the credentials configured for 'Sql:{identifier}' may perform this operation.",
                "Set CommandTimeout, or the step timeout, higher when the statement is simply slow.",
            ],
            null,
            innerException);
    }

    private static string Summarize(string statement)
    {
        string collapsed = string.Join(' ', statement.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= 500 ? collapsed : collapsed[..500] + "...";
    }

    private SqlExecutionFailedException(string friendlyMessage, IReadOnlyList<string> recoverySteps, IReadOnlyList<string>? availableOptions, Exception? innerException)
        : base(friendlyMessage, recoverySteps, availableOptions, innerException)
    {
    }
}
