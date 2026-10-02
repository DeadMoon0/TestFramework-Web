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

        // One declaration among any number: the redactor is built from all of them together, so this one and a
        // RedactHeaders call in code both count whichever is registered last.
        serviceCollection.AddSingleton(configProvider.LoadRedactionOptions(configuration));
    }
}
