using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Configuration;
using TestFramework.Web.Site.Configuration;
using TestFramework.Web.Sql.Configuration;
using TestFramework.Web.Stub.Configuration;

namespace TestFramework.Web.Extensions;

/// <summary>
/// Turns the Web package on for a service collection built without a config instance.
/// </summary>
public static class WebServiceCollectionExtension
{
    /// <summary>
    /// Declares the <c>Api</c>, <c>Sql</c>, <c>Stub</c> and <c>Site</c> configuration sections, each read by its
    /// default provider.
    /// </summary>
    /// <remarks>Uses an empty <see cref="WebConfigProviders"/>; the other overload states it.</remarks>
    /// <param name="serviceCollection">The service collection to declare the sections in.</param>
    /// <param name="configuration">The configuration root the sections are read from.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection AddWeb(this IServiceCollection serviceCollection, IConfiguration configuration)
        => serviceCollection.AddWeb(configuration, new WebConfigProviders());

    /// <summary>
    /// Declares the <c>Api</c>, <c>Sql</c>, <c>Stub</c> and <c>Site</c> configuration sections.
    /// </summary>
    /// <remarks>
    /// All four, always. Every store is registered even when its section is absent, so an environment that
    /// provides the resource at run time has somewhere to publish it, and a step that names a missing entry is
    /// told about the entry rather than about a package nobody switched on.
    /// </remarks>
    /// <param name="serviceCollection">The service collection to declare the sections in.</param>
    /// <param name="configuration">The configuration root the sections are read from.</param>
    /// <param name="providers">The reader for each section; an unset reader uses that section's default.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection AddWeb(this IServiceCollection serviceCollection, IConfiguration configuration, WebConfigProviders providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        new ApiConfigLoader(providers.Api ?? new DefaultApiConfigProvider()).LoadAllConfigs(configuration, serviceCollection);
        new SqlConfigLoader(providers.Sql ?? new DefaultSqlConfigProvider()).LoadAllConfigs(configuration, serviceCollection);
        new StubConfigLoader(providers.Stub ?? new DefaultStubConfigProvider()).LoadAllConfigs(configuration, serviceCollection);
        new SiteConfigLoader(providers.Site ?? new DefaultSiteConfigProvider()).LoadAllConfigs(configuration, serviceCollection);
        return serviceCollection;
    }
}
