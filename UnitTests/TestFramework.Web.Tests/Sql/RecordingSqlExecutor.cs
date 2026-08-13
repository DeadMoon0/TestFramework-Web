using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Web.Sql.Execution;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// A statement the executor was asked to run.
/// </summary>
/// <param name="Identifier">The SQL identifier.</param>
/// <param name="Statement">The statement text.</param>
/// <param name="Parameters">The bound parameter values.</param>
public sealed record RecordedSqlCall(string Identifier, string Statement, IReadOnlyDictionary<string, object?> Parameters)
{
    /// <summary>
    /// Returns whether the statement contains a fragment, ignoring case.
    /// </summary>
    /// <param name="fragment">The fragment to look for.</param>
    public bool Contains(string fragment) => Statement.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// An executor that records statements and returns canned results, so the SQL surface can be tested
/// end to end through real timelines without a database.
/// </summary>
public sealed class RecordingSqlExecutor : ISqlExecutor
{
    private readonly List<RecordedSqlCall> _calls = [];

    /// <summary>
    /// Every statement the executor was asked to run, in order.
    /// </summary>
    public IReadOnlyList<RecordedSqlCall> Calls => _calls;

    /// <summary>
    /// Result returned by execute statements.
    /// </summary>
    public int ExecuteResult { get; set; } = 1;

    /// <summary>
    /// Produces the scalar result for a statement.
    /// </summary>
    public Func<RecordedSqlCall, object?> ScalarResult { get; set; } = _ => 0;

    /// <summary>
    /// Produces the rows returned for a query statement.
    /// </summary>
    public Func<RecordedSqlCall, IEnumerable> QueryResult { get; set; } = _ => Array.Empty<object>();

    /// <inheritdoc />
    public Task<int> ExecuteAsync(string identifier, string statement, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        Record(identifier, statement, parameters);
        return Task.FromResult(ExecuteResult);
    }

    /// <inheritdoc />
    public Task<TValue?> ScalarAsync<TValue>(string identifier, string statement, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        RecordedSqlCall call = Record(identifier, statement, parameters);
        object? value = ScalarResult(call);
        return Task.FromResult(value is null ? default : (TValue?)Convert.ChangeType(value, typeof(TValue), System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TRow>> QueryAsync<TRow>(string identifier, string statement, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        RecordedSqlCall call = Record(identifier, statement, parameters);
        IReadOnlyList<TRow> rows = [.. QueryResult(call).Cast<TRow>()];
        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public string Describe(string identifier) => $"recorded/{identifier}";

    /// <summary>
    /// Returns the recorded statements whose text contains a fragment.
    /// </summary>
    /// <param name="fragment">The fragment to look for.</param>
    public IReadOnlyList<RecordedSqlCall> CallsContaining(string fragment) => [.. _calls.Where(call => call.Contains(fragment))];

    private RecordedSqlCall Record(string identifier, string statement, IReadOnlyDictionary<string, object?> parameters)
    {
        RecordedSqlCall call = new(identifier, statement, parameters);
        _calls.Add(call);
        return call;
    }
}
