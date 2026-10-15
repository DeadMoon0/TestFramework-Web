using System;
using TestFramework.Config.Builder.InstanceBuilder;
using TestFramework.Web.Configuration;

namespace TestFramework.Web.Extensions;

/// <summary>
/// Extension methods for loading web configuration in timeline config builders.
/// </summary>
public static class WebConfigExtension
{
    /// <summary>
    /// Loads the <c>Api</c>, <c>Sql</c>, <c>Stub</c> and <c>Site</c> configuration sections into the timeline config builder.
    /// </summary>
    /// <remarks>Uses an empty <see cref="WebConfigProviders"/>; the other overload states it.</remarks>
    /// <param name="builder">The config instance builder.</param>
    /// <returns>The builder for fluent chaining.</returns>
    public static IConfigInstanceBuilder LoadWebConfig(this IConfigInstanceBuilder builder)
        => builder.LoadWebConfig(new WebConfigProviders());

    /// <summary>
    /// Loads the <c>Api</c>, <c>Sql</c>, <c>Stub</c> and <c>Site</c> configuration sections into the timeline config builder.
    /// </summary>
    /// <remarks>
    /// The same declaration as <see cref="WebServiceCollectionExtension.AddWeb(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration, WebConfigProviders)"/>,
    /// read from the configuration the builder holds.
    /// </remarks>
    /// <param name="builder">The config instance builder.</param>
    /// <param name="providers">The reader for each section; an unset reader uses that section's default.</param>
    /// <returns>The builder for fluent chaining.</returns>
    public static IConfigInstanceBuilder LoadWebConfig(this IConfigInstanceBuilder builder, WebConfigProviders providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        builder.AddService((services, configuration) => services.AddWeb(configuration, providers));
        return builder;
    }
}
