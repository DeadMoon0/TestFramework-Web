using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TestFramework.Web.Configuration;

internal sealed class ApiConfigLoader(IApiConfigProvider configProvider)
{
    internal void LoadAllConfigs(IConfiguration configuration, IServiceCollection serviceCollection)
    {
        // Always register the store itself so environment-backed runs can hydrate identifiers
        // later even when the static config source did not define them.
        WebConfigStore<ApiConfig> store = new();
        serviceCollection.AddSingleton(store);

        foreach (string identifier in configProvider.LoadAllApiIdentifier(configuration))
            store.AddConfig(identifier, configProvider.LoadApiConfig(configuration, identifier));
    }
}
