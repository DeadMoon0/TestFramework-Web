using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web.Site.Exceptions;

/// <summary>
/// Thrown when the configuration for a site identifier is missing or unusable.
/// </summary>
public sealed class SiteConfigurationValidationException : TimelineFrameworkException
{
    /// <summary>
    /// Creates an exception for a site identifier that has no registered configuration.
    /// </summary>
    /// <param name="identifier">The identifier that could not be resolved.</param>
    /// <param name="knownIdentifiers">The identifiers that are registered.</param>
    /// <returns>The exception describing the missing registration.</returns>
    public static SiteConfigurationValidationException MissingIdentifier(string identifier, IEnumerable<string> knownIdentifiers)
    {
        string[] known = [.. knownIdentifiers.OrderBy(x => x, StringComparer.Ordinal)];
        return new SiteConfigurationValidationException(
            $"No site configuration was registered for identifier '{identifier}'.",
            [
                $"Add a 'Site:{identifier}' entry with a BaseUrl.",
                "Call LoadWebConfig() on the ConfigInstance builder so the Site section is loaded.",
                "When an environment hosts the site, make sure SetEnv(...) runs before the step.",
            ],
            known.Length == 0 ? ["(no site identifiers are registered)"] : known);
    }

    /// <summary>
    /// Creates an exception for a configuration value that is present but unusable.
    /// </summary>
    /// <param name="identifier">The identifier being resolved.</param>
    /// <param name="propertyName">The configuration property at fault.</param>
    /// <param name="problem">A description of why the value cannot be used.</param>
    /// <returns>The exception describing the invalid value.</returns>
    public static SiteConfigurationValidationException InvalidValue(string identifier, string propertyName, string problem)
        => new(
            $"Site '{identifier}' has an unusable value for '{propertyName}': {problem}",
            [$"Correct 'Site:{identifier}:{propertyName}' in the configuration used by this run."]);

    private SiteConfigurationValidationException(string friendlyMessage, IReadOnlyList<string> recoverySteps, IReadOnlyList<string>? availableOptions = null)
        : base(friendlyMessage, recoverySteps, availableOptions ?? [])
    {
    }
}
