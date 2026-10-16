using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Config;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Environment;
using TestFramework.Core.Events;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.Web.Stub.Admin;

namespace TestFramework.Web.Stub.Steps;

/// <summary>
/// The first matching call a stub received while the run waited for it.
/// </summary>
public sealed record StubCalledResult : StepResultContext
{
    internal StubCalledResult(string stubIdentifier, StubCall call, TimeSpan waited)
    {
        this.StubIdentifier = stubIdentifier;
        this.Call = call;
        this.Waited = waited;
    }

    /// <summary>
    /// The stub that was waited on.
    /// </summary>
    public string StubIdentifier { get; }

    /// <summary>
    /// The call that arrived.
    /// </summary>
    public StubCall Call { get; }

    /// <summary>
    /// How long the run waited for it.
    /// </summary>
    public TimeSpan Waited { get; }

    /// <summary>
    /// Returns a readable description of the event.
    /// </summary>
    public override string ToString() => $"'{StubIdentifier}' received {Call} after {Waited:g}";
}

/// <summary>
/// Waits until a stub is asked for something.
/// </summary>
/// <remarks>
/// Waiting is done by polling the stub's request log, not by a callback from the stub into the test.
/// That is what lets the same wait work against a stub in another process or another container,
/// which could never call back into this one.
/// </remarks>
public sealed class StubCalledEvent : Event<StubCalledEvent, StubCalledResult>, IHasEnvironmentRequirements
{
    private readonly StubIdentifier _stubIdentifier;
    private readonly string _method;
    private readonly string _path;
    private readonly StubHeaderFilters _headerFilters = new();
    private VariableReference<string>? _bodyContains;

    /// <summary>
    /// Creates the event.
    /// </summary>
    /// <param name="stubIdentifier">The stub to wait on.</param>
    /// <param name="method">The method the awaited call must have.</param>
    /// <param name="path">The path the awaited call must have.</param>
    internal StubCalledEvent(StubIdentifier stubIdentifier, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(stubIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _stubIdentifier = stubIdentifier;
        _method = method;
        _path = StubPathMatcher.Normalize(path)!;
    }

    /// <summary>
    /// Narrows the wait to a call whose body contains a text.
    /// </summary>
    /// <param name="text">The variable carrying the text the body must contain.</param>
    /// <remarks>
    /// Without this, a run that produces several calls to the same endpoint completes on the first
    /// one, which is rarely the one the test meant.
    /// </remarks>
    public StubCalledEvent WithBodyContaining(VariableReference<string> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        _bodyContains = text;
        return this;
    }

    /// <summary>
    /// Narrows the wait to a call carrying a header value.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The value the header must have.</param>
    /// <remarks>
    /// On a stub shared with other runs this is the only construct that gives truly isolated
    /// evidence. The timeline cannot stamp a correlation id on the call — the application under test
    /// makes it — but an application that forwards <c>traceparent</c> or a correlation header the
    /// test already set can be waited on precisely.
    /// </remarks>
    public StubCalledEvent WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        _headerFilters.Add(name, value);
        return this;
    }

    /// <summary>
    /// Narrows the wait to a call carrying a header with the value a variable holds when the event runs.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The variable carrying the value the header must have.</param>
    /// <remarks>
    /// The form for a stub shared between runs: each run waits on its own correlation id rather than on one
    /// fixed when the timeline was built.
    /// </remarks>
    public StubCalledEvent WithHeader(string name, VariableReference<string> value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        _headerFilters.Add(name, value);
        return this;
    }

    /// <inheritdoc />
    public override string Name => "Stub Called";

    /// <inheritdoc />
    public override string Description => $"Waits until '{_stubIdentifier}' receives {_method} {_path}";

    /// <inheritdoc />
    public override bool DoesReturn => true;

    /// <inheritdoc />
    public IReadOnlyCollection<EnvironmentRequirement> GetEnvironmentRequirements(VariableStore variableStore)
        => [new EnvironmentRequirement(WebResourceKinds.Stub, _stubIdentifier)];

    /// <inheritdoc />
    public override void DeclareIO(StepIOContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (_bodyContains?.Identifier is { } identifier)
            contract.Input(identifier, typeof(string));

        _headerFilters.Declare(contract);
    }

    /// <inheritdoc />
    public override Step<StubCalledResult> Clone()
    {
        StubCalledEvent clone = new(_stubIdentifier, _method, _path);
        if (_bodyContains is { } bodyContains)
            clone.WithBodyContaining(bodyContains);

        _headerFilters.CopyTo(clone._headerFilters);

        return clone.WithClonedOptions(this);
    }

    /// <inheritdoc />
    public override async Task<StubCalledResult?> DoEventPolling(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context.Logger);

        StubConfig config = context.Configured<StubConfig>(_stubIdentifier);
        StubAdminClient admin = StubConfigResolver.CreateAdminClient(context, _stubIdentifier);
        string? expectedBody = _bodyContains?.GetRequiredValue(context.Variables, "body text");
        IReadOnlyDictionary<string, string> headers = _headerFilters.Resolve(context.Variables);

        // Without the window, a wait on a shared stub can complete on a call another run made before
        // this one even started.
        DateTimeOffset? watermark = StubObservationScope.WatermarkFor(context.Services, _stubIdentifier);
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // The step timeout is the only limit, so a caller controls how long to wait with
        // WithTimeout(...) rather than with a second competing setting here.
        while (true)
        {
            context.Deadline.Token.ThrowIfCancellationRequested();

            foreach (StubCall call in await admin.GetCallsAsync(context.Deadline.Token).ConfigureAwait(false))
            {
                if (!Matches(call, expectedBody, headers, watermark))
                    continue;

                stopwatch.Stop();
                context.Logger.LogInformation("Stub '{0}' received {1} {2} after {3}.", _stubIdentifier.ToString(), _method, _path, stopwatch.Elapsed);
                return new StubCalledResult(_stubIdentifier, call, stopwatch.Elapsed);
            }

            await Task.Delay(config.PollInterval, context.Deadline.Token).ConfigureAwait(false);
        }
    }

    private bool Matches(StubCall call, string? expectedBody, IReadOnlyDictionary<string, string> headers, DateTimeOffset? watermark)
        => StubCallMatcher.IsInScope(call, watermark)
        && string.Equals(call.Method, _method, StringComparison.OrdinalIgnoreCase)
        && StubPathMatcher.Matches(call.Path, _path)
        && StubCallMatcher.HasHeaders(call, headers)
        && (expectedBody is null || call.Body?.Contains(expectedBody, StringComparison.Ordinal) == true);
}
