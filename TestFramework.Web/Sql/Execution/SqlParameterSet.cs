using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Variables;

namespace TestFramework.Web.Sql.Execution;

/// <summary>
/// Variable-backed parameters for a SQL statement.
/// </summary>
/// <remarks>
/// Parameters hold variable references rather than values so a statement, like a request, is built
/// once and run many times with different data.
/// </remarks>
public sealed class SqlParameterSet
{
    private readonly List<(string Name, VariableReferenceGeneric Value)> _parameters = [];

    /// <summary>
    /// Adds a parameter bound to a variable.
    /// </summary>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The variable carrying the value.</param>
    public SqlParameterSet Add<TValue>(string name, VariableReference<TValue> value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        _parameters.Add((name.TrimStart('@'), value));
        return this;
    }

    /// <summary>
    /// The parameter names, without the leading marker.
    /// </summary>
    public IReadOnlyList<string> Names => [.. _parameters.Select(parameter => parameter.Name)];

    /// <summary>
    /// The identifier-backed variables these parameters depend on.
    /// </summary>
    public IReadOnlyList<VariableReferenceGeneric> Inputs => [.. _parameters.Where(parameter => parameter.Value.HasIdentifier).Select(parameter => parameter.Value)];

    /// <summary>
    /// Resolves every parameter against the variable store.
    /// </summary>
    /// <param name="variableStore">The variable store for the current run.</param>
    public IReadOnlyDictionary<string, object?> Resolve(VariableStore variableStore)
    {
        Dictionary<string, object?> resolved = new(StringComparer.Ordinal);
        foreach ((string name, VariableReferenceGeneric value) in _parameters)
            resolved[name] = value.GetValueGeneric(variableStore);

        return resolved;
    }

    /// <summary>
    /// Creates a copy, so a cloned step does not share mutable state with the original definition.
    /// </summary>
    public SqlParameterSet Clone()
    {
        SqlParameterSet clone = new();
        clone._parameters.AddRange(_parameters);
        return clone;
    }
}
