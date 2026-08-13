using System;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Web.Sql.Artifacts;
using TestFramework.Web.Sql.Steps;
using TestFramework.Web.Sql.Steps.IsLive;

namespace TestFramework.Web;

/// <summary>
/// Typed result helpers for SQL timeline steps and row artifacts.
/// </summary>
/// <remarks>
/// Every entry point returns the framework's own <see cref="ValueHandle{T}"/>, so SQL assertions are
/// signalled to the debugging UI and participate in <c>run.AssertionScope()</c> like any other.
/// </remarks>
public static class WebSqlResultExtensions
{
    /// <summary>
    /// Starts an assertion chain for the value read by a scalar step.
    /// </summary>
    /// <typeparam name="TValue">The scalar value type.</typeparam>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    public static ValueHandle<TValue?> SqlScalar<TValue>(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);

        SqlScalarResult<TValue> result = run.Step(label).LastResult.Result as SqlScalarResult<TValue>
            ?? throw ResultTypeMismatch(run, label, typeof(SqlScalarResult<TValue>));

        return run.Assert(result.Value, $"'{label}' scalar from {result}");
    }

    /// <summary>
    /// Starts an assertion chain for the number of rows a statement changed.
    /// </summary>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    public static ValueHandle<int> SqlAffectedRows(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);

        SqlExecuteResult result = run.Step(label).LastResult.Result as SqlExecuteResult
            ?? throw ResultTypeMismatch(run, label, typeof(SqlExecuteResult));

        return run.Assert(result.AffectedRows, $"'{label}' affected rows of {result}");
    }

    /// <summary>
    /// Starts an assertion chain for the result of a SQL liveness probe.
    /// </summary>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    public static ValueHandle<SqlIsLiveResult> SqlProbe(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);

        SqlIsLiveResult result = run.Step(label).LastResult.Result as SqlIsLiveResult
            ?? throw ResultTypeMismatch(run, label, typeof(SqlIsLiveResult));

        return run.Assert(result, $"'{label}' probe");
    }

    /// <summary>
    /// Starts an assertion chain for the row behind a SQL row artifact.
    /// </summary>
    /// <typeparam name="TRow">The row model type.</typeparam>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="artifactIdentifier">The artifact identifier.</param>
    public static ValueHandle<TRow> SqlRow<TRow>(this TimelineRun run, string artifactIdentifier)
        where TRow : class
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Artifact<SqlRowArtifactData<TRow>>(artifactIdentifier).Select(data => data.Row);
    }

    private static InvalidOperationException ResultTypeMismatch(TimelineRun run, string label, Type expected)
    {
        StepHandle handle = run.Step(label);
        return new InvalidOperationException(
            $"Step '{handle.Label ?? handle.Step.Name}' did not produce a {expected.Name}. "
            + $"Its last result was '{handle.LastResult.Result?.GetType().Name ?? "null"}'.");
    }
}
