namespace TestFramework.Web.Identifier;

/// <summary>
/// Logical identifier used to resolve a configured REST API.
/// </summary>
/// <param name="Identifier">The configuration key that names the API entry.</param>
public record ApiIdentifier(string Identifier)
{
    /// <summary>
    /// Converts a typed identifier to its raw string representation.
    /// </summary>
    /// <param name="id">The typed identifier instance.</param>
    public static implicit operator string(ApiIdentifier id) => id.Identifier;

    /// <summary>
    /// Converts a raw configuration key to a typed API identifier.
    /// </summary>
    /// <param name="id">The raw configuration key.</param>
    public static implicit operator ApiIdentifier(string id) => new ApiIdentifier(id);

    /// <summary>
    /// Returns the raw identifier value.
    /// </summary>
    public override string ToString() => Identifier;
}
