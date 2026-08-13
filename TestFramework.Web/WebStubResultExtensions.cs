using System;
using System.Collections.Generic;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Web.Stub.Admin;
using TestFramework.Web.Stub.Steps;

namespace TestFramework.Web;

/// <summary>
/// Typed result helpers for stub timeline steps.
/// </summary>
public static class WebStubResultExtensions
{
    /// <summary>
    /// Starts an assertion chain for the calls a stub observation collected.
    /// </summary>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    public static ValueHandle<IReadOnlyList<StubCall>> StubCalls(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);

        StubCallsResult result = run.Step(label).LastResult.Result as StubCallsResult
            ?? throw ResultTypeMismatch(run, label, typeof(StubCallsResult));

        return run.Assert(result.Calls, $"'{label}' calls of {result}");
    }

    /// <summary>
    /// Starts an assertion chain for the calls a stub received but had no mapping for.
    /// </summary>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    /// <remarks>
    /// Asserting that this is empty catches the application under test calling something the test
    /// never declared, which no assertion on a response body would reveal.
    /// </remarks>
    public static ValueHandle<IReadOnlyList<StubCall>> StubUnmatchedCalls(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);

        StubCallsResult result = run.Step(label).LastResult.Result as StubCallsResult
            ?? throw ResultTypeMismatch(run, label, typeof(StubCallsResult));

        return run.Assert(result.UnmatchedCalls, $"'{label}' unmatched calls of {result}");
    }

    /// <summary>
    /// Starts an assertion chain for the call a stub wait produced.
    /// </summary>
    /// <param name="run">The completed timeline run.</param>
    /// <param name="label">The step label.</param>
    public static ValueHandle<StubCall> StubCall(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);

        StubCalledResult result = run.Step(label).LastResult.Result as StubCalledResult
            ?? throw ResultTypeMismatch(run, label, typeof(StubCalledResult));

        return run.Assert(result.Call, $"'{label}' call of {result}");
    }

    private static InvalidOperationException ResultTypeMismatch(TimelineRun run, string label, Type expected)
    {
        StepHandle handle = run.Step(label);
        return new InvalidOperationException(
            $"Step '{handle.Label ?? handle.Step.Name}' did not produce a {expected.Name}. "
            + $"Its last result was '{handle.LastResult.Result?.GetType().Name ?? "null"}'.");
    }
}
