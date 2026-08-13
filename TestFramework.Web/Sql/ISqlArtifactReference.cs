namespace TestFramework.Web.Sql;

/// <summary>
/// Exposes the SQL identifier an artifact reference targets.
/// </summary>
/// <remarks>
/// Environment providers need to know which databases a run touches so they can start only those.
/// This interface exists so they can ask, instead of reflecting over property names.
/// </remarks>
public interface ISqlArtifactReference
{
    /// <summary>
    /// The SQL identifier this reference resolves against.
    /// </summary>
    SqlIdentifier SqlIdentifier { get; }
}
