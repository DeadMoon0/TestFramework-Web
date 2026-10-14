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
    /// <param name="builder">The config instance builder.</param>
    /// <returns>The builder for fluent chaining.</returns>
    /// <remarks>
    /// Every store is registered even when its section is absent, so an environment that provides
    /// the resource at run time has somewhere to publish it.
    /// </remarks>
    public static IConfigInstanceBuilder LoadWebConfig(this IConfigInstanceBuilder builder)
        => builder.LoadWebConfig(null);

    /// <summary>
    /// Loads the <c>Api</c>, <c>Sql</c>, <c>Stub</c> and <c>Site</c> configuration sections into the timeline config builder.
    /// </summary>
    /// <param name="builder">The config instance builder.</param>
    /// <param name="provider">Optional custom configuration provider. Uses <see cref="DefaultApiConfigProvider"/> when omitted.</param>
    /// <returns>The builder for fluent chaining.</returns>
    /// <remarks>
    /// Every store is registered even when its section is absent, so an environment that provides
    /// the resource at run time has somewhere to publish it.
    /// </remarks>
    public static IConfigInstanceBuilder LoadWebConfig(this IConfigInstanceBuilder builder, IApiConfigProvider? provider)
    {
        builder.AddService((services, configuration) =>
        {
            services.LoadWebConfigs(configuration, provider);
            services.LoadWebSqlConfigs(configuration);
            services.LoadWebStubConfigs(configuration);
            services.LoadWebSiteConfigs(configuration);
        });
        return builder;
    }
}
