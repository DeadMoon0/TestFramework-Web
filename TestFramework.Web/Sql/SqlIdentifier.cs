namespace TestFramework.Web.Sql;

/// <summary>
/// Logical identifier used to resolve a configured SQL Server database.
/// </summary>
/// <param name="Identifier">The configuration key that names the database entry.</param>
public record SqlIdentifier(string Identifier)
{
    /// <summary>
    /// Converts a typed identifier to its raw string representation.
    /// </summary>
    /// <param name="id">The typed identifier instance.</param>
    public static implicit operator string(SqlIdentifier id) => id.Identifier;

    /// <summary>
    /// Converts a raw configuration key to a typed SQL identifier.
    /// </summary>
    /// <param name="id">The raw configuration key.</param>
    public static implicit operator SqlIdentifier(string id) => new SqlIdentifier(id);

    /// <summary>
    /// Returns the raw identifier value.
    /// </summary>
    public override string ToString() => Identifier;
}
