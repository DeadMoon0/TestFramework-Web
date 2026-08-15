using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Variables;
using TestFramework.Web.Stub;
using TestFramework.Web.Stub.Admin;
using TestFramework.Web.Stub.Steps;
using Xunit;

namespace TestFramework.Web.Tests.Stub;

/// <summary>
/// Covers the scoping that keeps one run's stub evidence apart from every other run's.
/// </summary>
/// <remarks>
/// A stub server's request log is global and its entries carry the stub's own clock, so these rules
/// are the only thing standing between a shared stub and an assertion that reads another run's call.
/// </remarks>
public class StubObservationScopeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static StubCall Call(string path, DateTimeOffset? receivedAt, params string[] headerPairs)
    {
        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index + 1 < headerPairs.Length; index += 2)
            headers[headerPairs[index]] = headerPairs[index + 1];

        return new StubCall("POST", path, null, null, headers, receivedAt, true);
    }

    [Fact]
    public void Watermark_ExcludesCallsAtOrBeforeIt()
    {
        Assert.False(StubCallMatcher.IsInScope(Call("/a", Noon.AddSeconds(-1)), Noon));
        Assert.False(StubCallMatcher.IsInScope(Call("/a", Noon), Noon));
        Assert.True(StubCallMatcher.IsInScope(Call("/a", Noon.AddSeconds(1)), Noon));
    }

    [Fact]
    public void ACallWithoutATimestamp_StaysInScope()
    {
        // The stub did not say when it arrived, so it cannot be excluded. Keeping too much is a
        // weaker guarantee; dropping it would be a wrong one.
        Assert.True(StubCallMatcher.IsInScope(Call("/a", null), Noon));
    }

    [Fact]
    public void WithoutAWatermark_EveryCallIsInScope()
        => Assert.True(StubCallMatcher.IsInScope(Call("/a", Noon.AddYears(-1)), null));

    [Fact]
    public void NewestTimestamp_IgnoresEntriesWithoutOne()
    {
        DateTimeOffset? newest = StubCallMatcher.NewestTimestamp(
            [Call("/a", Noon), Call("/b", null), Call("/c", Noon.AddSeconds(5))]);

        Assert.Equal(Noon.AddSeconds(5), newest);
    }

    [Fact]
    public void NewestTimestamp_IsNullWhenNothingIsTimestamped()
        => Assert.Null(StubCallMatcher.NewestTimestamp([Call("/a", null)]));

    [Fact]
    public void Scope_RemembersAndClearsPerIdentifier()
    {
        StubObservationScope scope = new();
        scope.SetWatermark("payments", Noon);
        scope.SetWatermark("shipping", Noon.AddMinutes(1));

        Assert.Equal(Noon, scope.GetWatermark("payments"));
        Assert.Equal(Noon.AddMinutes(1), scope.GetWatermark("shipping"));

        scope.ClearWatermark("payments");
        Assert.Null(scope.GetWatermark("payments"));
        Assert.Equal(Noon.AddMinutes(1), scope.GetWatermark("shipping"));
    }

    [Fact]
    public void Scope_TreatsAnEmptyLogAsNoWatermark()
    {
        StubObservationScope scope = new();
        scope.SetWatermark("payments", Noon);
        scope.SetWatermark("payments", null);

        Assert.Null(scope.GetWatermark("payments"));
    }

    [Fact]
    public void TheUntimedCallWarning_IsRaisedOncePerStub()
    {
        StubObservationScope scope = new();

        Assert.True(scope.ShouldWarnAboutUntimedCalls("payments"));
        Assert.False(scope.ShouldWarnAboutUntimedCalls("payments"));
        Assert.True(scope.ShouldWarnAboutUntimedCalls("shipping"));
    }

    [Fact]
    public void HeaderFilter_MatchesOneValueOfAMultiValuedHeader()
    {
        StubCall call = Call("/api/charges", Noon, "traceparent", "00-abc-1, 00-abc-2");

        Assert.True(StubCallMatcher.HasHeaders(call, new Dictionary<string, string> { ["traceparent"] = "00-abc-2" }));
        Assert.False(StubCallMatcher.HasHeaders(call, new Dictionary<string, string> { ["traceparent"] = "00-abc-3" }));
        Assert.False(StubCallMatcher.HasHeaders(call, new Dictionary<string, string> { ["x-correlation-id"] = "00-abc-1" }));
    }

    [Fact]
    public void HeaderFilter_NarrowsTheCallsAStepReports()
    {
        FilterProbe probe = new("payments", "POST", "/api/charges");
        probe.WithHeader("x-correlation-id", "run-b");

        StubCall[] log =
        [
            Call("/api/charges", Noon.AddSeconds(1), "x-correlation-id", "run-a"),
            Call("/api/charges", Noon.AddSeconds(2), "x-correlation-id", "run-b"),
        ];

        IReadOnlyList<StubCall> matching = probe.Apply(log, watermark: null);

        Assert.Single(matching);
        Assert.Equal("run-b", matching[0].Headers["x-correlation-id"]);
    }

    [Fact]
    public void TheWatermark_NarrowsTheCallsAStepReports()
    {
        FilterProbe probe = new("payments", "POST", "/api/charges");

        StubCall[] log =
        [
            Call("/api/charges", Noon.AddSeconds(-5)),
            Call("/api/charges", Noon.AddSeconds(5)),
        ];

        Assert.Equal(2, probe.Apply(log, watermark: null).Count);
        Assert.Single(probe.Apply(log, Noon));
    }

    [Theory]
    [InlineData("/api/charges", "/api/charges", true)]
    [InlineData("/API/Charges", "/api/charges", true)]
    [InlineData("/api/charges/42", "/api/charges", false)]
    [InlineData("/api/charges/42", "/api/charges/*", true)]
    [InlineData("/api/charges/42/refunds", "/api/charges/*", true)]
    [InlineData("/api/charges", "/api/charges/*", false)]
    [InlineData("/api/v1.0/charges", "/api/v1.0/*", true)]
    [InlineData("/api/vXY0/charges", "/api/v1.0/*", false)]
    [InlineData("/anything", "*", true)]
    public void PathFilters_UnderstandTheSameWildcardTheMappingsDo(string path, string pattern, bool expected)
        => Assert.Equal(expected, StubPathMatcher.Matches(path, pattern));

    [Fact]
    public void APathFilterWithoutALeadingSlash_StillMatches()
    {
        // A logged path always has one; Called already normalized, Calls silently did not.
        FilterProbe probe = new("payments", null, "api/charges");

        Assert.Single(probe.Apply([Call("/api/charges", Noon)], watermark: null));
    }

    [Fact]
    public void AWildcardPathFilter_MatchesTheCallsAMappingWouldHaveAnswered()
    {
        FilterProbe probe = new("payments", null, "/api/charges/*");

        StubCall[] log = [Call("/api/charges/42", Noon), Call("/api/refunds/42", Noon)];

        StubCall matching = Assert.Single(probe.Apply(log, watermark: null));
        Assert.Equal("/api/charges/42", matching.Path);
    }

    /// <summary>
    /// Exposes the protected filter of <see cref="StubStepBase{TResult}"/> so its rules can be
    /// asserted without a stub server in the loop.
    /// </summary>
    private sealed class FilterProbe(string identifier, string? method, string? path)
        : StubStepBase<StubCallsResult>(identifier, method, path)
    {
        public override string Name => "Filter Probe";

        public override string Description => "Test double";

        public override Step<StubCallsResult> Clone() => new FilterProbe(StubIdentifier, Method, Path);

        public override StepInstance<Step<StubCallsResult>, StubCallsResult> GetInstance() => new(this);

        public void WithHeader(string name, string value) => HeaderFilters[name] = value;

        public IReadOnlyList<StubCall> Apply(IEnumerable<StubCall> calls, DateTimeOffset? watermark) => Filter(calls, watermark);

        public override Task<StubCallsResult?> Execute(
            IServiceProvider serviceProvider,
            VariableStore variableStore,
            ArtifactStore artifactStore,
            ScopedLogger logger,
            CancellationToken cancellationToken)
            => Task.FromResult<StubCallsResult?>(new StubCallsResult(StubIdentifier, [], []));
    }
}
