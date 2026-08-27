using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config.Configuration;

namespace TestFramework.Web.Configuration;

internal sealed class ApiConfigLoader(IApiConfigProvider configProvider)
{
    internal void LoadAllConfigs(IConfiguration configuration, IServiceCollection serviceCollection)
    {
        // The same entries again, this time as resources the run's graph knows about - so a step asking where
        // an API is gets one answer whether a person wrote it down or a container published it later.
        serviceCollection.AddConfigShape(new ApiShape(configProvider));

        WebRedactionOptions redactionOptions = configProvider.LoadRedactionOptions(configuration);
        serviceCollection.AddSingleton(redactionOptions);
        serviceCollection.AddSingleton(new Http.HttpHeaderRedactor(redactionOptions));
    }
}
