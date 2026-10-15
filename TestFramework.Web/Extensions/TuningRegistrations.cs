using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TestFramework.Core.Exceptions;
using TestFramework.Web.Sql.Model;

namespace TestFramework.Web.Extensions;

/// <summary>
/// How Web's tuning calls register, so that calling one twice adds up instead of the last call winning.
/// </summary>
/// <remarks>
/// Each call registered its result as a singleton, and the container hands out the last one: a second
/// <c>AddWebSqlModels</c> silently dropped the first project's models, and the symptom surfaced two layers away
/// as a model with no key. Each call now registers a declaration, and one factory combines them all.
/// </remarks>
internal static class TuningRegistrations
{
    /// <summary>
    /// Adds a set of model mappings; the registry is built from every set, and a model mapped by two is refused.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Declares the mappings.</param>
    public static void AddModels(IServiceCollection services, Action<SqlModelBuilder> configure)
    {
        services.AddSingleton(new SqlModelDeclaration(configure));
        services.TryAddSingleton(provider =>
        {
            SqlModelBuilder combined = new();
            foreach (SqlModelDeclaration declaration in provider.GetServices<SqlModelDeclaration>())
            {
                SqlModelBuilder own = new();
                declaration.Configure(own);
                combined.Absorb(own);
            }

            return SqlModelRegistry.CreateDefault(combined);
        });
    }

    /// <summary>
    /// Adds a projection of a tuning record; the record a run sees is every projection applied in order.
    /// </summary>
    /// <typeparam name="TConfig">The tuning record.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="project">Projects the record so far onto the one to use.</param>
    public static void AddProjection<TConfig>(IServiceCollection services, Func<TConfig, TConfig> project)
        where TConfig : class, new()
    {
        services.AddSingleton(new TuningProjection<TConfig>(project));
        services.TryAddSingleton(provider => provider.GetServices<TuningProjection<TConfig>>()
            .Aggregate(new TConfig(), (config, projection) => projection.Project(config)));
    }

    /// <summary>
    /// Registers the one implementation of a service a run may have, refusing a second rather than replacing the first.
    /// </summary>
    /// <typeparam name="TService">The service.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="instance">The implementation.</param>
    /// <param name="registeredBy">The call that registers it, for the refusal.</param>
    public static void AddSingleOnly<TService>(IServiceCollection services, TService instance, string registeredBy)
        where TService : class
    {
        if (services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(TService)) is { } existing)
        {
            string name = existing.ImplementationInstance?.GetType().Name ?? existing.ImplementationType?.Name ?? typeof(TService).Name;

            throw new FrameworkConfigurationException(
                $"{registeredBy} was called twice: '{name}' is already registered, and a run uses one {typeof(TService).Name}.")
            {
                RecoverySteps = [$"Call {registeredBy} once, with the implementation this run should use."],
            };
        }

        services.AddSingleton(instance);
    }

    private sealed record SqlModelDeclaration(Action<SqlModelBuilder> Configure);

    private sealed record TuningProjection<TConfig>(Func<TConfig, TConfig> Project);
}
