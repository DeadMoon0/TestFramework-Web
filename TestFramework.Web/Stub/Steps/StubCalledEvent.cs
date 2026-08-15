using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
/// <param name="StubIdentifier">The stub that was waited on.</param>
/// <param name="Call">The call that arrived.</param>
/// <param name="Waited">How long the run waited for it.</param>
public sealed record StubCalledResult(string StubIdentifier, StubCall Call, TimeSpan Waited) : StepResultContext
{
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
    private readonly Dictionary<string, string> _headerFilters = new(StringComparer.OrdinalIgnoreCase);
    private VariableReference<string>? _bodyContains;

    /// <summary>
    /// Creates the event.
    /// </summary>
    /// <param name="stubIdentifier">The stub to wait on.</param>
    /// <param name="method">The method the awaited call must have.</param>
    /// <param name="path">The path the awaited call must have.</param>
    public StubCalledEvent(StubIdentifier stubIdentifier, string method, string path)
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
        _headerFilters[name] = value;
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
        => [new EnvironmentRequirement(WebEnvironmentResourceKinds.Stub, _stubIdentifier)];

    /// <inheritdoc />
    public override void DeclareIO(StepIOContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (_bodyContains?.Identifier is { } identifier)
            contract.Inputs.Add(new StepIOEntry(identifier.Identifier, StepIOKind.Variable, true, typeof(string)));
    }

    /// <inheritdoc />
    public override Step<StubCalledResult> Clone()
    {
        StubCalledEvent clone = new(_stubIdentifier, _method, _path);
        if (_bodyContains is { } bodyContains)
            clone.WithBodyContaining(bodyContains);

        foreach ((string name, string value) in _headerFilters)
            clone._headerFilters[name] = value;

        return clone.WithClonedOptions(this);
    }

    /// <inheritdoc />
    public override async Task<StubCalledResult?> DoEventPolling(
        IServiceProvider serviceProvider,
        VariableStore variableStore,
        ArtifactStore artifactStore,
        ScopedLogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logger);

        StubConfig config = StubConfigResolver.Resolve(serviceProvider, _stubIdentifier);
        StubAdminClient admin = StubConfigResolver.CreateAdminClient(serviceProvider, _stubIdentifier);
        string? expectedBody = _bodyContains?.GetRequiredValue(variableStore, "body text");

        // Without the window, a wait on a shared stub can complete on a call another run made before
        // this one even started.
        DateTimeOffset? watermark = StubObservationScope.WatermarkFor(serviceProvider, _stubIdentifier);
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // The step timeout is the only limit, so a caller controls how long to wait with
        // WithTimeOut(...) rather than with a second competing setting here.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (StubCall call in await admin.GetCallsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!Matches(call, expectedBody, watermark))
                    continue;

                stopwatch.Stop();
                logger.LogInformation("Stub '{0}' received {1} {2} after {3}.", _stubIdentifier.ToString(), _method, _path, stopwatch.Elapsed);
                return new StubCalledResult(_stubIdentifier, call, stopwatch.Elapsed);
            }

            await Task.Delay(config.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool Matches(StubCall call, string? expectedBody, DateTimeOffset? watermark)
        => StubCallMatcher.IsInScope(call, watermark)
        && string.Equals(call.Method, _method, StringComparison.OrdinalIgnoreCase)
        && StubPathMatcher.Matches(call.Path, _path)
        && StubCallMatcher.HasHeaders(call, _headerFilters)
        && (expectedBody is null || call.Body?.Contains(expectedBody, StringComparison.Ordinal) == true);
}
