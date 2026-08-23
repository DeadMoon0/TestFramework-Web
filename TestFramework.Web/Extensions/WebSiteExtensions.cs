using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Site.Configuration;

namespace TestFramework.Web.Extensions;

/// <summary>
/// Extension methods for configuring site support.
/// </summary>
public static class WebSiteExtensions
{
    /// <summary>
    /// Loads site configuration entries into the service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection to register the site config store in.</param>
    /// <param name="configuration">The configuration root from which to load settings.</param>
    /// <param name="provider">Optional custom configuration provider. Uses <see cref="DefaultSiteConfigProvider"/> when omitted.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection LoadWebSiteConfigs(this IServiceCollection serviceCollection, IConfiguration configuration, ISiteConfigProvider? provider = null)
    {
        SiteConfigLoader loader = new(provider ?? new DefaultSiteConfigProvider());
        loader.LoadAllConfigs(configuration, serviceCollection);
        return serviceCollection;
    }
}
