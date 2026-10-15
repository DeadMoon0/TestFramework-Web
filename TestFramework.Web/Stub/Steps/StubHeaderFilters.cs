using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;

namespace TestFramework.Web.Stub.Steps;

/// <summary>
/// The header values a stub step or event filters calls on, each given as text or read from a variable.
/// </summary>
/// <remarks>
/// A header filter was text fixed when the timeline was built, so every run of one timeline filtered on the same
/// value - which isolates nothing on a stub several runs share, the very case the filter exists for. A value read
/// from a variable is resolved when the step runs, so each run can wait on its own correlation id.
/// </remarks>
internal sealed class StubHeaderFilters
{
    private readonly Dictionary<string, Filter> _filters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether any header is filtered on.
    /// </summary>
    public bool IsEmpty => _filters.Count == 0;

    /// <summary>
    /// Filters on a header having a value the timeline states.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The value.</param>
    public void Add(string name, string value) => _filters[name] = new Filter(name, Var.Const(value), value);

    /// <summary>
    /// Filters on a header having the value a variable holds when the step runs.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The variable.</param>
    public void Add(string name, VariableReference<string> value) => _filters[name] = new Filter(name, value, null);

    /// <summary>
    /// Copies every filter into another set, for a clone.
    /// </summary>
    /// <param name="target">The set to copy into.</param>
    public void CopyTo(StubHeaderFilters target)
    {
        foreach ((string name, Filter filter) in _filters)
            target._filters[name] = filter;
    }

    /// <summary>
    /// Declares the variables the filters read.
    /// </summary>
    /// <param name="contract">The step's contract.</param>
    public void Declare(StepIOContract contract)
    {
        foreach (Filter filter in _filters.Values)
        {
            if (filter.Value.Identifier is { } identifier)
                contract.Input(identifier, typeof(string));
        }
    }

    /// <summary>
    /// The header values this run filters on.
    /// </summary>
    /// <param name="store">The run's variables.</param>
    /// <returns>The values, keyed by header name.</returns>
    public IReadOnlyDictionary<string, string> Resolve(VariableStore store)
        => _filters.Values.ToDictionary(
            filter => filter.Name,
            filter => filter.Value.GetRequiredValue(store, $"stub header filter '{filter.Name}'"),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The filters as a description says them: a stated value as itself, a variable by its name.
    /// </summary>
    /// <returns>The description, such as <c>x-correlation-id={correlationId}</c>.</returns>
    public string Describe()
        => string.Join(", ", _filters.Values.Select(filter => $"{filter.Name}={filter.Literal ?? $"{{{filter.Value.Identifier}}}"}"));

    private sealed record Filter(string Name, VariableReference<string> Value, string? Literal);
}
