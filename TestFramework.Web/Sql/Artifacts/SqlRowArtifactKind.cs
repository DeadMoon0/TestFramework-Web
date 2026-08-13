using TestFramework.Core.Artifacts;

namespace TestFramework.Web.Sql.Artifacts;

/// <summary>
/// Static artifact kind for table rows.
/// </summary>
/// <typeparam name="TRow">The row model type.</typeparam>
public sealed class SqlRowArtifactKind<TRow> : ArtifactKind<SqlRowArtifactDescriber<TRow>, SqlRowArtifactData<TRow>, SqlRowArtifactReference<TRow>>, IStaticArtifactKind<SqlRowArtifactKind<TRow>>
    where TRow : class
{
    /// <summary>
    /// Singleton artifact kind instance.
    /// </summary>
    public static SqlRowArtifactKind<TRow> Kind { get; } = new SqlRowArtifactKind<TRow>();
}
