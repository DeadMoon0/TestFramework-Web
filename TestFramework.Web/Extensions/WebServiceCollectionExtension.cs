using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Configuration;

namespace TestFramework.Web.Extensions;

/// <summary>
/// Extension methods for configuring web services in the dependency injection container.
/// </summary>
public static class WebServiceCollectionExtension
{
    /// <summary>
    /// Loads API configuration entries into the service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection to register the API config store in.</param>
    /// <param name="configuration">The configuration root from which to load settings.</param>
    /// <param name="provider">Optional custom configuration provider. Uses <see cref="DefaultApiConfigProvider"/> when omitted.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection LoadWebConfigs(this IServiceCollection serviceCollection, IConfiguration configuration, IApiConfigProvider? provider = null)
    {
        provider ??= new DefaultApiConfigProvider();
        ApiConfigLoader loader = new(provider);
        loader.LoadAllConfigs(configuration, serviceCollection);
        return serviceCollection;
    }
}
