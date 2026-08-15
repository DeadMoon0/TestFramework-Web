using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace TestFramework.Web.Stub;

/// <summary>
/// Remembers, per stub identifier, the point in the stub's request log this run starts observing at.
/// </summary>
/// <remarks>
/// Registered alongside the stub configuration store, so its lifetime is the run's service provider
/// rather than the process: a static cache would reintroduce exactly the cross-run leak the
/// watermark exists to prevent. The stored value is a timestamp from the stub's own clock, never
/// from the test host's, because those two clocks are not comparable when the stub runs elsewhere.
/// </remarks>
internal sealed class StubObservationScope
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _watermarks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _warnedAboutUntimedCalls = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the scope registered for the run, or <see langword="null"/> when none is.
    /// </summary>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    public static StubObservationScope? Resolve(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return serviceProvider.GetService<StubObservationScope>();
    }

    /// <summary>
    /// Returns the watermark recorded for a stub, or <see langword="null"/> when the whole log counts.
    /// </summary>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    /// <param name="identifier">The stub identifier.</param>
    public static DateTimeOffset? WatermarkFor(IServiceProvider serviceProvider, string identifier)
        => Resolve(serviceProvider)?.GetWatermark(identifier);

    /// <summary>
    /// Records the newest timestamp already in the log, so later observations ignore it and everything before.
    /// </summary>
    /// <param name="identifier">The stub identifier.</param>
    /// <param name="receivedAt">The newest logged timestamp, or <see langword="null"/> for an empty log.</param>
    public void SetWatermark(string identifier, DateTimeOffset? receivedAt)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        if (receivedAt is { } value)
            _watermarks[identifier] = value;
        else
            _watermarks.TryRemove(identifier, out _);
    }

    /// <summary>
    /// Drops any watermark for a stub, which is what clearing the server log makes true.
    /// </summary>
    /// <param name="identifier">The stub identifier.</param>
    public void ClearWatermark(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        _watermarks.TryRemove(identifier, out _);
    }

    /// <summary>
    /// Returns the watermark recorded for a stub, or <see langword="null"/> when there is none.
    /// </summary>
    /// <param name="identifier">The stub identifier.</param>
    public DateTimeOffset? GetWatermark(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return _watermarks.TryGetValue(identifier, out DateTimeOffset value) ? value : null;
    }

    /// <summary>
    /// Returns <see langword="true"/> the first time a stub is seen logging a call without a timestamp.
    /// </summary>
    /// <param name="identifier">The stub identifier.</param>
    /// <remarks>
    /// Once per stub, not once per call: the condition is a property of the server, and a warning per
    /// call would bury the run in noise for a single fact.
    /// </remarks>
    public bool ShouldWarnAboutUntimedCalls(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return _warnedAboutUntimedCalls.TryAdd(identifier, 0);
    }
}
