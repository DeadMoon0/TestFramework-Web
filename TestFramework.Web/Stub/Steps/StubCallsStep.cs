using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
public sealed record StubCallsResult : StepResultContext
{
    internal StubCallsResult(string stubIdentifier, IReadOnlyList<StubCall> calls, IReadOnlyList<StubCall> unmatchedCalls)
    {
        this.StubIdentifier = stubIdentifier;
        this.Calls = calls;
        this.UnmatchedCalls = unmatchedCalls;
    }

    /// <summary>
    /// The stub that was inspected.
    /// </summary>
    public string StubIdentifier { get; }

    /// <summary>
    /// The matching calls, oldest first.
    /// </summary>
    public IReadOnlyList<StubCall> Calls { get; }

    /// <summary>
    /// Calls the stub received but had no mapping for.
    /// </summary>
    public IReadOnlyList<StubCall> UnmatchedCalls { get; }

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
    private protected StubStepBase(StubIdentifier stubIdentifier, string? method, string? path)
    {
        ArgumentNullException.ThrowIfNull(stubIdentifier);

        StubIdentifier = stubIdentifier;
        Method = method;

        // A logged path always starts with a slash. Accepting both spellings stops
        // Calls("x", path: "api/charges") from silently matching nothing while the identical string
        // works on Called.
        Path = StubPathMatcher.Normalize(path);
    }

    /// <summary>
    /// Header values a call must carry to count.
    /// </summary>
    private protected StubHeaderFilters HeaderFilters { get; } = new();

    /// <summary>
    /// The stub this step inspects.
    /// </summary>
    public StubIdentifier StubIdentifier { get; }

    /// <summary>
    /// The method calls must have to count, when one was given.
    /// </summary>
    public string? Method { get; private protected set; }

    /// <summary>
    /// The path calls must have to count, when one was given.
    /// </summary>
    public string? Path { get; private protected set; }

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
        ArgumentNullException.ThrowIfNull(contract);
        HeaderFilters.Declare(contract);
    }

    /// <summary>
    /// Returns the calls matching this step's filter, within the run's observation window.
    /// </summary>
    /// <param name="calls">Every call the stub received.</param>
    /// <param name="watermark">The newest timestamp that predates this run, or <see langword="null"/> for the whole log.</param>
    /// <param name="variables">The run's variables, which header filters may read.</param>
    private protected IReadOnlyList<StubCall> Filter(IEnumerable<StubCall> calls, DateTimeOffset? watermark, VariableStore variables)
    {
        ArgumentNullException.ThrowIfNull(calls);

        IReadOnlyDictionary<string, string> headers = HeaderFilters.Resolve(variables);

        return [.. calls.Where(call =>
            StubCallMatcher.IsInScope(call, watermark)
            && (Method is null || string.Equals(call.Method, Method, StringComparison.OrdinalIgnoreCase))
            && (Path is null || StubPathMatcher.Matches(call.Path, Path))
            && StubCallMatcher.HasHeaders(call, headers))];
    }

    /// <summary>
    /// Warns once per stub when a watermark is active but the server logs calls without a timestamp.
    /// </summary>
    /// <param name="serviceProvider">The services, for the once-per-stub warning state.</param>
    /// <param name="calls">Every call the stub received.</param>
    /// <param name="watermark">The active watermark, or <see langword="null"/> when none is.</param>
    /// <param name="logger">The run's logger.</param>
    protected void WarnAboutUntimedCalls(
        IServiceProvider serviceProvider,
        IEnumerable<StubCall> calls,
        DateTimeOffset? watermark,
        ScopedLogger logger)
    {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(logger);

        if (watermark is null || !calls.Any(call => call.ReceivedAt is null))
            return;

        if (StubObservationScope.Resolve(serviceProvider)?.ShouldWarnAboutUntimedCalls(StubIdentifier) != true)
            return;

        logger.LogWarning(
            $"Stub '{StubIdentifier}' logs calls without a timestamp. Those calls cannot be placed against the reset watermark, "
            + "so they are counted as this run's. On a stub shared with other runs, treat the evidence as advisory.");
    }

    /// <summary>
    /// A readable description of the filter, for logs and messages.
    /// </summary>
    private protected string FilterDescription
    {
        get
        {
            string headers = HeaderFilters.IsEmpty ? string.Empty : " with " + HeaderFilters.Describe();

            return $"{Method ?? "any method"} {Path ?? "any path"}{headers}";
        }
    }
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
    /// Creates the step over every call the stub logged; <c>WebExt.Stub.Calls(...)</c> is the way to make one.
    /// </summary>
    /// <param name="stubIdentifier">The stub to inspect.</param>
    internal StubCallsStep(StubIdentifier stubIdentifier)
        : base(stubIdentifier, null, null)
    {
    }

    private StubCallsStep(StubIdentifier stubIdentifier, string? method, string? path)
        : base(stubIdentifier, method, path)
    {
    }

    /// <summary>
    /// Narrows the observation to calls made with one method.
    /// </summary>
    /// <param name="method">The method a call must have.</param>
    /// <returns>The step for fluent chaining.</returns>
    /// <remarks>
    /// A modifier like <see cref="WithHeader(string, string)"/>, not a constructor argument: each filter is its own method, so a
    /// filter added later - a query, a body - is a new method rather than one more optional parameter that every
    /// compiled caller would have depended on.
    /// </remarks>
    public StubCallsStep WithMethod(HttpMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);

        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        Method = method.Method;
        return this;
    }

    /// <summary>
    /// Narrows the observation to calls made to one path, which may use <c>*</c> wildcards.
    /// </summary>
    /// <param name="path">The path a call must have; with or without its leading slash.</param>
    /// <returns>The step for fluent chaining.</returns>
    public StubCallsStep WithPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        Path = StubPathMatcher.Normalize(path);
        return this;
    }

    /// <summary>
    /// Narrows the observation to calls carrying a header value.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The value the header must have.</param>
    /// <returns>The step for fluent chaining.</returns>
    /// <remarks>
    /// On a stub shared with other runs this is the only construct that gives truly isolated
    /// evidence: the timeline cannot stamp a correlation id on the calls, because the application
    /// under test makes them, but any application that forwards <c>traceparent</c> or a correlation
    /// header the test already set on its own request can be filtered on here.
    /// </remarks>
    public StubCallsStep WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        HeaderFilters.Add(name, value);
        return this;
    }

    /// <summary>
    /// Counts only calls carrying a header with the value a variable holds when the step runs.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The variable carrying the value the header must have.</param>
    /// <returns>The step for fluent chaining.</returns>
    /// <remarks>
    /// The form for a stub shared between runs: each run filters on its own correlation id rather than on one
    /// fixed when the timeline was built.
    /// </remarks>
    public StubCallsStep WithHeader(string name, VariableReference<string> value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        HeaderFilters.Add(name, value);
        return this;
    }

    /// <inheritdoc />
    public override string Name => "Stub Calls";

    /// <inheritdoc />
    public override string Description => $"Reads the calls '{StubIdentifier}' received for {FilterDescription}";

    /// <inheritdoc />
    public override Step<StubCallsResult> Clone()
    {
        StubCallsStep clone = new(StubIdentifier, Method, Path);
        HeaderFilters.CopyTo(clone.HeaderFilters);

        return clone.WithClonedOptions(this);
    }

    /// <inheritdoc />
    public override StepInstance<Step<StubCallsResult>, StubCallsResult> GetInstance() => new(this);

    /// <inheritdoc />
    public override async Task<StubCallsResult?> Execute(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context.Logger);

        StubAdminClient admin = StubConfigResolver.CreateAdminClient(context, StubIdentifier);
        IReadOnlyList<StubCall> all = await admin.GetCallsAsync(context.Deadline.Token).ConfigureAwait(false);

        // Everything this step reports is bounded by the window the reset opened, so a shared stub's
        // older traffic is not read as this run's evidence.
        DateTimeOffset? watermark = StubObservationScope.WatermarkFor(context.Services, StubIdentifier);
        WarnAboutUntimedCalls(context.Services, all, watermark, context.Logger);

        IReadOnlyList<StubCall> matching = Filter(all, watermark, context.Variables);
        IReadOnlyList<StubCall> unmatched = [.. all.Where(call => !call.Matched && StubCallMatcher.IsInScope(call, watermark))];

        context.Logger.LogInformation("Stub '{0}' received {1} call(s) for {2}.", StubIdentifier.ToString(), matching.Count, FilterDescription);

        // An unmatched call is the application asking for something the test never declared, which
        // is worth surfacing even when it is not what this step filtered for. Query and body come
        // along because "no mapping answered this" is usually a one-character difference, and
        // without them the next step is always another debugging round.
        foreach (StubCall call in unmatched)
            context.Logger.LogWarning($"Stub '{StubIdentifier}' had no mapping for {Describe(call)}.");

        return new StubCallsResult(StubIdentifier, matching, unmatched);
    }

    private static string Describe(StubCall call)
    {
        string query = string.IsNullOrEmpty(call.Query) ? string.Empty : $"?{call.Query.TrimStart('?')}";
        string body = string.IsNullOrWhiteSpace(call.Body) ? string.Empty : $" body: {Excerpt(call.Body)}";

        return $"{call.Method} {call.Path}{query}{body}";
    }

    private static string Excerpt(string body)
    {
        string collapsed = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= 200 ? collapsed : collapsed[..200] + "...";
    }
}

/// <summary>
/// Opens this run's observation window on a stub, so later observations only see calls made after
/// this point.
/// </summary>
/// <remarks>
/// By default nothing is deleted: the step records the newest timestamp already in the log and later
/// steps ignore everything at or before it. That is the only behaviour that is safe on a stub other
/// runs share. Configure <c>ResetMode: ClearServerLog</c> on a stub this run owns to get the log
/// itself emptied.
/// </remarks>
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
    public override string Description => $"Starts a fresh observation window on '{StubIdentifier}'";

    /// <inheritdoc />
    public override StepExecutionPhase Phase => StepExecutionPhase.Prepare;

    /// <inheritdoc />
    public override Step<StubCallsResult> Clone() => new StubResetStep(StubIdentifier).WithClonedOptions(this);

    /// <inheritdoc />
    public override StepInstance<Step<StubCallsResult>, StubCallsResult> GetInstance() => new(this);

    /// <inheritdoc />
    public override async Task<StubCallsResult?> Execute(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context.Logger);

        StubConfig config = context.Configured<StubConfig>(StubIdentifier);
        StubAdminClient admin = StubConfigResolver.CreateAdminClient(context, StubIdentifier);
        StubObservationScope? scope = StubObservationScope.Resolve(context.Services);

        if (config.ResetMode == StubResetMode.ClearServerLog)
        {
            await admin.ResetCallsAsync(context.Deadline.Token).ConfigureAwait(false);
            scope?.ClearWatermark(StubIdentifier);

            context.Logger.LogInformation("Stub '{0}' request log deleted on the server.", StubIdentifier.ToString());
            return new StubCallsResult(StubIdentifier, [], []);
        }

        // The watermark comes from the stub's own clock, so no skew between this host and the stub
        // can shift the boundary. DateTimeOffset.UtcNow here would be meaningless for a stub that
        // runs somewhere else.
        IReadOnlyList<StubCall> existing = await admin.GetCallsAsync(context.Deadline.Token).ConfigureAwait(false);
        DateTimeOffset? watermark = StubCallMatcher.NewestTimestamp(existing);
        scope?.SetWatermark(StubIdentifier, watermark);

        if (scope is null)
        {
            context.Logger.LogWarning(
                $"Stub '{StubIdentifier}' could not record an observation watermark: no stub configuration was loaded into the run. "
                + "Later observations will see the whole request log.");
        }
        else if (watermark is { } cutoff)
        {
            context.Logger.LogInformation(
                "Stub '{0}' observation window starts after {1}; the {2} call(s) already logged are ignored.",
                StubIdentifier.ToString(),
                cutoff.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                existing.Count);
        }
        else
        {
            context.Logger.LogInformation("Stub '{0}' has no timestamped calls logged; every call counts as this run's.", StubIdentifier.ToString());
        }

        return new StubCallsResult(StubIdentifier, [], []);
    }
}
