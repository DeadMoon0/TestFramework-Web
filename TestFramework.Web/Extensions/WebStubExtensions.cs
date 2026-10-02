using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Stub.Configuration;

namespace TestFramework.Web.Extensions;

/// <summary>
/// Extension methods for configuring stub support.
/// </summary>
public static class WebStubExtensions
{
    /// <summary>
    /// Loads stub configuration entries into the service collection.
    /// </summary>
    /// <remarks>Uses <c>null</c> for <c>provider</c>; the other overload states it.</remarks>
    /// <param name="serviceCollection">The service collection to register the stub config store in.</param>
    /// <param name="configuration">The configuration root from which to load settings.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection LoadWebStubConfigs(this IServiceCollection serviceCollection, IConfiguration configuration)
        => serviceCollection.LoadWebStubConfigs(configuration, null);

    /// <summary>
    /// Loads stub configuration entries into the service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection to register the stub config store in.</param>
    /// <param name="configuration">The configuration root from which to load settings.</param>
    /// <param name="provider">Optional custom configuration provider. Uses <see cref="DefaultStubConfigProvider"/> when omitted.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection LoadWebStubConfigs(this IServiceCollection serviceCollection, IConfiguration configuration, IStubConfigProvider? provider)
    {
        StubConfigLoader loader = new(provider ?? new DefaultStubConfigProvider());
        loader.LoadAllConfigs(configuration, serviceCollection);
        return serviceCollection;
    }
}
