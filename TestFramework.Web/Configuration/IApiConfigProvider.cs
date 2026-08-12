using Microsoft.Extensions.Configuration;

namespace TestFramework.Web.Configuration;

/// <summary>
/// Reads API configuration entries from an <see cref="IConfiguration"/> source.
/// </summary>
/// <remarks>
/// Implement this to move the API settings to a different section shape than the default
/// <c>Api:&lt;identifier&gt;</c> layout.
/// </remarks>
public interface IApiConfigProvider
{
    /// <summary>
    /// Returns every API identifier present in the configuration source.
    /// </summary>
    /// <param name="configuration">The configuration source to inspect.</param>
    string[] LoadAllApiIdentifier(IConfiguration configuration);

    /// <summary>
    /// Reads the configuration for a single API identifier.
    /// </summary>
    /// <param name="configuration">The configuration source to read from.</param>
    /// <param name="identifier">The identifier to read.</param>
    ApiConfig LoadApiConfig(IConfiguration configuration, string identifier);
}
