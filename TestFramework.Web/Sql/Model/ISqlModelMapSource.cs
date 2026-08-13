using System;

namespace TestFramework.Web.Sql.Model;

/// <summary>
/// Produces a <see cref="SqlModelMap"/> for a model type.
/// </summary>
/// <remarks>
/// Sources are consulted in order of decreasing explicitness, so a registration always beats an
/// attribute and an attribute always beats a convention. Adding a further source - reading an ORM's
/// own metadata, for example - requires no change anywhere else.
/// </remarks>
public interface ISqlModelMapSource
{
    /// <summary>
    /// Attempts to map a model type.
    /// </summary>
    /// <param name="modelType">The type to map.</param>
    /// <param name="map">The resulting map when this source can produce one.</param>
    /// <returns><see langword="true"/> when the source produced a map.</returns>
    bool TryResolve(Type modelType, out SqlModelMap? map);
}
