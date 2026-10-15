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
    /// <remarks>Uses <c>null</c> for <c>provider</c>; the other overload states it.</remarks>
    /// <param name="serviceCollection">The service collection to declare the SQL section in.</param>
    /// <param name="configuration">The configuration root from which to load settings.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection LoadWebSqlConfigs(this IServiceCollection serviceCollection, IConfiguration configuration)
        => serviceCollection.LoadWebSqlConfigs(configuration, null);

    /// <summary>
    /// Loads SQL configuration entries into the service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection to declare the SQL section in.</param>
    /// <param name="configuration">The configuration root from which to load settings.</param>
    /// <param name="provider">Optional custom configuration provider. Uses <see cref="DefaultSqlConfigProvider"/> when omitted.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection LoadWebSqlConfigs(this IServiceCollection serviceCollection, IConfiguration configuration, ISqlConfigProvider? provider)
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

        TuningRegistrations.AddModels(serviceCollection, configure);
        return serviceCollection;
    }

    /// <summary>
    /// Configures SQL step behaviour for the run.
    /// </summary>
    /// <param name="builder">The config instance builder.</param>
    /// <param name="configure">Projects the configuration so far onto the one to use; several calls apply in order.</param>
    /// <returns>The builder for fluent chaining.</returns>
    public static IConfigInstanceBuilder ConfigureSqlSteps(this IConfigInstanceBuilder builder, Func<SqlStepConfig, SqlStepConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.AddService(services => TuningRegistrations.AddProjection(services, configure));
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

        builder.AddService(services => TuningRegistrations.AddSingleOnly(services, provider, nameof(UseSqlCredentials)));
        return builder;
    }

    /// <summary>
    /// Registers the factory SQL steps create their executor with, in place of the built-in ADO.NET executor.
    /// </summary>
    /// <param name="builder">The config instance builder.</param>
    /// <param name="factory">The factory; see <see cref="Sql.Execution.ISqlExecutorFactory"/> for what its executors must do.</param>
    /// <returns>The builder for fluent chaining.</returns>
    public static IConfigInstanceBuilder UseSqlExecutor(this IConfigInstanceBuilder builder, Sql.Execution.ISqlExecutorFactory factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        builder.AddService(services => TuningRegistrations.AddSingleOnly(services, factory, nameof(UseSqlExecutor)));
        return builder;
    }
}
