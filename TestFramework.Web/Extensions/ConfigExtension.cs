using TestFramework.Config.Builder.InstanceBuilder;
using TestFramework.Web.Configuration;

namespace TestFramework.Web.Extensions;

/// <summary>
/// Extension methods for loading web configuration in timeline config builders.
/// </summary>
public static class ConfigExtension
{
    /// <summary>
    /// Loads the <c>Api</c> configuration section into the timeline config builder.
    /// </summary>
    /// <param name="builder">The config instance builder.</param>
    /// <param name="provider">Optional custom configuration provider. Uses <see cref="DefaultApiConfigProvider"/> when omitted.</param>
    /// <returns>The builder for fluent chaining.</returns>
    public static IConfigInstanceBuilder LoadWebConfig(this IConfigInstanceBuilder builder, IApiConfigProvider? provider = null)
    {
        builder.AddService((services, configuration) => services.LoadWebConfigs(configuration, provider));
        return builder;
    }
}
