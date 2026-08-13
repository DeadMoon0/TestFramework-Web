using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web.Sql.Exceptions;

/// <summary>
/// Thrown when the configuration for a SQL identifier is missing or unusable.
/// </summary>
public sealed class SqlConfigurationValidationException : TimelineFrameworkException
{
    /// <summary>
    /// Creates an exception for a SQL identifier that has no registered configuration.
    /// </summary>
    /// <param name="identifier">The identifier that could not be resolved.</param>
    /// <param name="knownIdentifiers">The identifiers that are registered.</param>
    /// <returns>The exception describing the missing registration.</returns>
    public static SqlConfigurationValidationException MissingIdentifier(string identifier, IEnumerable<string> knownIdentifiers)
    {
        string[] known = [.. knownIdentifiers.OrderBy(x => x, StringComparer.Ordinal)];
        return new SqlConfigurationValidationException(
            $"No SQL configuration was registered for identifier '{identifier}'.",
            [
                $"Add a 'Sql:{identifier}' entry with either ConnectionString or Server and Database.",
                "Call LoadWebConfig() on the ConfigInstance builder so the Sql section is loaded.",
                "When an environment provides the database, make sure SetEnv(...) runs before the step.",
            ],
            known.Length == 0 ? ["(no SQL identifiers are registered)"] : known);
    }

    /// <summary>
    /// Creates an exception for a configuration value that is present but unusable.
    /// </summary>
    /// <param name="identifier">The identifier being resolved.</param>
    /// <param name="propertyName">The configuration property at fault.</param>
    /// <param name="problem">A description of why the value cannot be used.</param>
    /// <returns>The exception describing the invalid value.</returns>
    public static SqlConfigurationValidationException InvalidValue(string identifier, string propertyName, string problem)
        => new(
            $"SQL '{identifier}' has an unusable value for '{propertyName}': {problem}",
            [$"Correct 'Sql:{identifier}:{propertyName}' in the configuration used by this run."]);

    private SqlConfigurationValidationException(string friendlyMessage, IReadOnlyList<string> recoverySteps, IReadOnlyList<string>? availableOptions = null)
        : base(friendlyMessage, recoverySteps, availableOptions)
    {
    }
}
