using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config.Builder.InstanceBuilder;
using TestFramework.Web.Sql.Configuration;
using TestFramework.Web.Sql.Model;
using TestFramework.Web.Sql.Steps;

namespace TestFramework.Web.Extensions;

/// <summary>
/// Extension methods for configuring SQL support.
/// </summary>
public static class WebSqlExtensions
{
    /// <summary>
    /// Loads SQL configuration entries into the service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection to register the SQL config store in.</param>
    /// <param name="configuration">The configuration root from which to load settings.</param>
    /// <param name="provider">Optional custom configuration provider. Uses <see cref="DefaultSqlConfigProvider"/> when omitted.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection LoadWebSqlConfigs(this IServiceCollection serviceCollection, IConfiguration configuration, ISqlConfigProvider? provider = null)
    {
        SqlConfigLoader loader = new(provider ?? new DefaultSqlConfigProvider());
        loader.LoadAllConfigs(configuration, serviceCollection);
        return serviceCollection;
    }

    /// <summary>
    /// Registers explicit model mappings for SQL row artifacts and queries.
    /// </summary>
    /// <param name="builder">The config instance builder.</param>
    /// <param name="configure">Declares the mappings.</param>
    /// <returns>The builder for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// .AddWebSqlModels(models =&gt; models.For&lt;Order&gt;().Table("Orders").Key(x =&gt; x.Id).Generated(x =&gt; x.Id))
    /// </code>
    /// </example>
    public static IConfigInstanceBuilder AddWebSqlModels(this IConfigInstanceBuilder builder, Action<SqlModelBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.AddService(services => services.AddWebSqlModels(configure));
        return builder;
    }

    /// <summary>
    /// Registers explicit model mappings in a service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection to register the registry in.</param>
    /// <param name="configure">Declares the mappings.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection AddWebSqlModels(this IServiceCollection serviceCollection, Action<SqlModelBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        ArgumentNullException.ThrowIfNull(configure);

        SqlModelBuilder modelBuilder = new();
        configure(modelBuilder);
        serviceCollection.AddSingleton(SqlModelRegistry.CreateDefault(modelBuilder));
        return serviceCollection;
    }

    /// <summary>
    /// Configures SQL step behaviour for the run.
    /// </summary>
    /// <param name="builder">The config instance builder.</param>
    /// <param name="configure">Projects the defaults onto the configuration to use.</param>
    /// <returns>The builder for fluent chaining.</returns>
    public static IConfigInstanceBuilder ConfigureSqlSteps(this IConfigInstanceBuilder builder, Func<SqlStepConfig, SqlStepConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.AddService(services => services.AddSingleton(configure(new SqlStepConfig())));
        return builder;
    }

    /// <summary>
    /// Registers a credential provider that overrides the configured SQL credentials at run time.
    /// </summary>
    /// <param name="builder">The config instance builder.</param>
    /// <param name="provider">The credential provider.</param>
    /// <returns>The builder for fluent chaining.</returns>
    public static IConfigInstanceBuilder UseSqlCredentials(this IConfigInstanceBuilder builder, Sql.ISqlCredentialProvider provider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(provider);

        builder.AddService(services => services.AddSingleton(provider));
        return builder;
    }
}
