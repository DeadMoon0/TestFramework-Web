using System;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config.Builder.InstanceBuilder;
using TestFramework.Web.Configuration;
using TestFramework.Web.Trigger;

namespace TestFramework.Web.Extensions;

/// <summary>
/// Extension methods for tuning API trigger behaviour and header redaction.
/// </summary>
public static class ApiTriggerConfigExtension
{
    /// <summary>
    /// Configures API trigger behaviour for the run.
    /// </summary>
    /// <param name="builder">The config instance builder.</param>
    /// <param name="configure">Projects the defaults onto the configuration to use.</param>
    /// <returns>The builder for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// ConfigInstance.FromJsonFile("local.testsettings.json")
    ///     .LoadWebConfig()
    ///     .ConfigureApiTrigger(config =&gt; config with { LogRequestHeaders = true })
    ///     .Build();
    /// </code>
    /// </example>
    public static IConfigInstanceBuilder ConfigureApiTrigger(this IConfigInstanceBuilder builder, Func<ApiTriggerConfig, ApiTriggerConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.AddService(services => services.ConfigureApiTrigger(configure));
        return builder;
    }

    /// <summary>
    /// Configures API trigger behaviour in a service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection to register the configuration in.</param>
    /// <param name="configure">Projects the defaults onto the configuration to use.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection ConfigureApiTrigger(this IServiceCollection serviceCollection, Func<ApiTriggerConfig, ApiTriggerConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        ArgumentNullException.ThrowIfNull(configure);

        serviceCollection.AddSingleton(configure(new ApiTriggerConfig()));
        return serviceCollection;
    }

    /// <summary>
    /// Adds header names to the redaction policy from code.
    /// </summary>
    /// <param name="builder">The config instance builder.</param>
    /// <param name="headerNames">The header names to redact.</param>
    /// <returns>The builder for fluent chaining.</returns>
    /// <remarks>
    /// Prefer the <c>Web:SensitiveHeaders</c> configuration section. Use this only when the names are
    /// not known until run time.
    /// </remarks>
    public static IConfigInstanceBuilder RedactHeaders(this IConfigInstanceBuilder builder, params string[] headerNames)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(headerNames);

        builder.AddService(services =>
        {
            WebRedactionOptions options = WebRedactionOptions.Default.With(headerNames);
            services.AddSingleton(options);
            services.AddSingleton(new Http.HttpHeaderRedactor(options));
        });

        return builder;
    }
}
