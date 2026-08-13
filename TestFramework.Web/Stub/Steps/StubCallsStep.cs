using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Environment;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.Web.Stub.Admin;

namespace TestFramework.Web.Stub.Steps;

/// <summary>
/// The calls a stub received, as observed at one point in the run.
/// </summary>
/// <param name="StubIdentifier">The stub that was inspected.</param>
/// <param name="Calls">The matching calls, oldest first.</param>
/// <param name="UnmatchedCalls">Calls the stub received but had no mapping for.</param>
public sealed record StubCallsResult(string StubIdentifier, IReadOnlyList<StubCall> Calls, IReadOnlyList<StubCall> UnmatchedCalls) : StepResultContext
{
    /// <summary>
    /// Returns a readable description of the observation.
    /// </summary>
    public override string ToString()
        => $"'{StubIdentifier}' received {Calls.Count} matching call(s)"
        + (UnmatchedCalls.Count > 0 ? $" and {UnmatchedCalls.Count} unmatched" : string.Empty);
}

/// <summary>
/// Shared behaviour for steps that read a stub's request log.
/// </summary>
/// <typeparam name="TResult">The step result type.</typeparam>
public abstract class StubStepBase<TResult> : Step<TResult>, IHasEnvironmentRequirements
    where TResult : StepResultContext
{
    /// <summary>
    /// Creates a stub step.
    /// </summary>
    /// <param name="stubIdentifier">The stub to inspect.</param>
    /// <param name="method">The method to filter by, or <see langword="null"/> for any.</param>
    /// <param name="path">The path to filter by, or <see langword="null"/> for any.</param>
    protected StubStepBase(StubIdentifier stubIdentifier, string? method, string? path)
    {
        ArgumentNullException.ThrowIfNull(stubIdentifier);

        StubIdentifier = stubIdentifier;
        Method = method;
        Path = path;
    }

    /// <summary>
    /// The stub this step inspects.
    /// </summary>
    public StubIdentifier StubIdentifier { get; }

    /// <summary>
    /// The method calls must have to count, when one was given.
    /// </summary>
    public string? Method { get; }

    /// <summary>
    /// The path calls must have to count, when one was given.
    /// </summary>
    public string? Path { get; }

    /// <inheritdoc />
    public override bool DoesReturn => true;

    /// <inheritdoc />
    public override StepExecutionPhase Phase => StepExecutionPhase.Observe;

    /// <inheritdoc />
    public IReadOnlyCollection<EnvironmentRequirement> GetEnvironmentRequirements(VariableStore variableStore)
        => [new EnvironmentRequirement(WebEnvironmentResourceKinds.Stub, StubIdentifier)];

    /// <inheritdoc />
    public override void DeclareIO(StepIOContract contract)
    {
    }

    /// <summary>
    /// Returns the calls matching this step's filter.
    /// </summary>
    /// <param name="calls">Every call the stub received.</param>
    protected IReadOnlyList<StubCall> Filter(IEnumerable<StubCall> calls)
        => [.. calls.Where(call =>
            (Method is null || string.Equals(call.Method, Method, StringComparison.OrdinalIgnoreCase))
            && (Path is null || string.Equals(call.Path, Path, StringComparison.OrdinalIgnoreCase)))];

    /// <summary>
    /// A readable description of the filter, for logs and messages.
    /// </summary>
    protected string FilterDescription => $"{Method ?? "any method"} {Path ?? "any path"}";
}

/// <summary>
/// Reads what a stub was asked for.
/// </summary>
/// <remarks>
/// The stub's own request log is the source, so this observes what the application under test really
/// sent rather than what it reported having sent.
/// </remarks>
public sealed class StubCallsStep : StubStepBase<StubCallsResult>
{
    /// <summary>
    /// Creates the step.
    /// </summary>
    /// <param name="stubIdentifier">The stub to inspect.</param>
    /// <param name="method">The method to filter by, or <see langword="null"/> for any.</param>
    /// <param name="path">The path to filter by, or <see langword="null"/> for any.</param>
    public StubCallsStep(StubIdentifier stubIdentifier, string? method = null, string? path = null)
        : base(stubIdentifier, method, path)
    {
    }

    /// <inheritdoc />
    public override string Name => "Stub Calls";

    /// <inheritdoc />
    public override string Description => $"Reads the calls '{StubIdentifier}' received for {FilterDescription}";

    /// <inheritdoc />
    public override Step<StubCallsResult> Clone() => new StubCallsStep(StubIdentifier, Method, Path).WithClonedOptions(this);

    /// <inheritdoc />
    public override StepInstance<Step<StubCallsResult>, StubCallsResult> GetInstance() => new(this);

    /// <inheritdoc />
    public override async Task<StubCallsResult?> Execute(
        IServiceProvider serviceProvider,
        VariableStore variableStore,
        ArtifactStore artifactStore,
        ScopedLogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logger);

        StubAdminClient admin = StubConfigResolver.CreateAdminClient(serviceProvider, StubIdentifier);
        IReadOnlyList<StubCall> all = await admin.GetCallsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StubCall> matching = Filter(all);
        IReadOnlyList<StubCall> unmatched = [.. all.Where(call => !call.Matched)];

        logger.LogInformation("Stub '{0}' received {1} call(s) for {2}.", StubIdentifier.ToString(), matching.Count, FilterDescription);

        // An unmatched call is the application asking for something the test never declared, which
        // is worth surfacing even when it is not what this step filtered for.
        foreach (StubCall call in unmatched)
            logger.LogWarning($"Stub '{StubIdentifier}' had no mapping for {call.Method} {call.Path}.");

        return new StubCallsResult(StubIdentifier, matching, unmatched);
    }
}

/// <summary>
/// Clears a stub's request log, so later observations only see calls made after this point.
/// </summary>
public sealed class StubResetStep : StubStepBase<StubCallsResult>
{
    /// <summary>
    /// Creates the step.
    /// </summary>
    /// <param name="stubIdentifier">The stub to reset.</param>
    public StubResetStep(StubIdentifier stubIdentifier)
        : base(stubIdentifier, null, null)
    {
    }

    /// <inheritdoc />
    public override string Name => "Stub Reset";

    /// <inheritdoc />
    public override string Description => $"Clears the request log of '{StubIdentifier}'";

    /// <inheritdoc />
    public override StepExecutionPhase Phase => StepExecutionPhase.Prepare;

    /// <inheritdoc />
    public override Step<StubCallsResult> Clone() => new StubResetStep(StubIdentifier).WithClonedOptions(this);

    /// <inheritdoc />
    public override StepInstance<Step<StubCallsResult>, StubCallsResult> GetInstance() => new(this);

    /// <inheritdoc />
    public override async Task<StubCallsResult?> Execute(
        IServiceProvider serviceProvider,
        VariableStore variableStore,
        ArtifactStore artifactStore,
        ScopedLogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logger);

        StubAdminClient admin = StubConfigResolver.CreateAdminClient(serviceProvider, StubIdentifier);
        await admin.ResetCallsAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Stub '{0}' request log cleared.", StubIdentifier.ToString());
        return new StubCallsResult(StubIdentifier, [], []);
    }
}
