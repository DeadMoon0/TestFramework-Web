using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web.Exceptions;

/// <summary>
/// Thrown when the configuration for an API identifier is missing or unusable.
/// </summary>
public sealed class ApiConfigurationValidationException : TimelineFrameworkException
{
    /// <summary>
    /// Creates an exception for an API identifier that has no registered configuration.
    /// </summary>
    /// <param name="identifier">The identifier that could not be resolved.</param>
    /// <param name="knownIdentifiers">The identifiers that are registered.</param>
    /// <returns>The exception describing the missing registration.</returns>
    public static ApiConfigurationValidationException MissingIdentifier(string identifier, IEnumerable<string> knownIdentifiers)
    {
        string[] known = [.. knownIdentifiers.OrderBy(x => x, System.StringComparer.Ordinal)];
        return new ApiConfigurationValidationException(
            $"No API configuration was registered for identifier '{identifier}'.",
            [
                $"Add an 'Api:{identifier}:BaseUrl' entry to the configuration used by this run.",
                "Call LoadWebConfig() on the ConfigInstance builder so the Api section is loaded.",
                "When an environment provides the API, make sure SetEnv(...) runs before the step.",
            ],
            known.Length == 0 ? ["(no API identifiers are registered)"] : known);
    }

    /// <summary>
    /// Creates an exception for a configuration value that is present but unusable.
    /// </summary>
    /// <param name="identifier">The identifier being resolved.</param>
    /// <param name="propertyName">The configuration property at fault.</param>
    /// <param name="problem">A description of why the value cannot be used.</param>
    /// <returns>The exception describing the invalid value.</returns>
    public static ApiConfigurationValidationException InvalidValue(string identifier, string propertyName, string problem)
        => new(
            $"API '{identifier}' has an unusable value for '{propertyName}': {problem}",
            [
                $"Correct 'Api:{identifier}:{propertyName}' in the configuration used by this run.",
            ]);

    /// <summary>
    /// Creates an exception for an authentication mode whose required values are absent.
    /// </summary>
    /// <param name="identifier">The identifier being resolved.</param>
    /// <param name="mode">The configured authentication mode.</param>
    /// <param name="requiredProperties">The properties the mode requires.</param>
    /// <returns>The exception describing the incomplete authentication setup.</returns>
    public static ApiConfigurationValidationException IncompleteAuth(string identifier, string mode, params string[] requiredProperties)
        => new(
            $"API '{identifier}' is configured for '{mode}' authentication but required values are missing.",
            [.. requiredProperties.Select(property => $"Set 'Api:{identifier}:{property}'.")]);

    private ApiConfigurationValidationException(string friendlyMessage, IReadOnlyList<string> recoverySteps, IReadOnlyList<string>? availableOptions = null)
        : base(friendlyMessage, recoverySteps, availableOptions)
    {
    }
}
