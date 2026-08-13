using System;
using TestFramework.Core.Artifacts;

namespace TestFramework.Web.Sql.Artifacts;

/// <summary>
/// A single table row.
/// </summary>
/// <typeparam name="TRow">The row model type.</typeparam>
public sealed class SqlRowArtifactData<TRow> : ArtifactData<SqlRowArtifactData<TRow>, SqlRowArtifactDescriber<TRow>, SqlRowArtifactReference<TRow>>
    where TRow : class
{
    /// <summary>
    /// Creates artifact data around a row model.
    /// </summary>
    /// <param name="row">The row.</param>
    public SqlRowArtifactData(TRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Row = row;
    }

    /// <summary>
    /// The row.
    /// </summary>
    public TRow Row { get; }

    /// <summary>
    /// Returns a readable description of the row.
    /// </summary>
    public override string ToString() => $"{typeof(TRow).Name} row";
}
