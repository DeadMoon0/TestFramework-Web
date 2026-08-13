using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Web.Sql.Exceptions;

namespace TestFramework.Web.Sql.Model;

/// <summary>
/// Resolves and caches model maps from the registered sources.
/// </summary>
public sealed class SqlModelRegistry
{
    private readonly IReadOnlyList<ISqlModelMapSource> _sources;
    private readonly ConcurrentDictionary<Type, SqlModelMap> _cache = new();

    /// <summary>
    /// Creates a registry over the provided sources, consulted in order.
    /// </summary>
    /// <param name="sources">The sources, most explicit first.</param>
    public SqlModelRegistry(IEnumerable<ISqlModelMapSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = [.. sources];
    }

    /// <summary>
    /// Creates the default registry: explicit registrations, then attributes, then convention.
    /// </summary>
    /// <param name="builder">The explicit registrations, when any.</param>
    public static SqlModelRegistry CreateDefault(SqlModelBuilder? builder = null)
        => new([
            new FluentSqlModelSource(builder ?? new SqlModelBuilder()),
            new DataAnnotationsSqlModelSource(),
            new ConventionSqlModelSource(),
        ]);

    /// <summary>
    /// Resolves the map for a model type.
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    public SqlModelMap Resolve<TModel>() => Resolve(typeof(TModel));

    /// <summary>
    /// Resolves the map for a model type.
    /// </summary>
    /// <param name="modelType">The model type.</param>
    /// <exception cref="SqlModelMapException">No source could map the type.</exception>
    public SqlModelMap Resolve(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        return _cache.GetOrAdd(modelType, type =>
        {
            foreach (ISqlModelMapSource source in _sources)
            {
                if (source.TryResolve(type, out SqlModelMap? map) && map is not null)
                    return map;
            }

            throw SqlModelMapException.NoKey(type);
        });
    }

    /// <summary>
    /// Returns the map for a model type when one can be produced.
    /// </summary>
    /// <param name="modelType">The model type.</param>
    /// <param name="map">The resolved map.</param>
    public bool TryResolve(Type modelType, out SqlModelMap? map)
    {
        try
        {
            map = Resolve(modelType);
            return true;
        }
        catch (SqlModelMapException)
        {
            map = null;
            return false;
        }
    }

    /// <summary>
    /// The sources this registry consults, in order.
    /// </summary>
    public IReadOnlyList<ISqlModelMapSource> Sources => _sources;

    /// <summary>
    /// Returns a readable description of the registry.
    /// </summary>
    public override string ToString() => $"SqlModelRegistry({string.Join(" -> ", _sources.Select(source => source.GetType().Name))})";
}
