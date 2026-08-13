namespace TestFramework.Web.Stub;

/// <summary>
/// Logical identifier used to resolve a configured stub server.
/// </summary>
/// <param name="Identifier">The configuration key that names the stub entry.</param>
public record StubIdentifier(string Identifier)
{
    /// <summary>
    /// Converts a typed identifier to its raw string representation.
    /// </summary>
    /// <param name="id">The typed identifier instance.</param>
    public static implicit operator string(StubIdentifier id) => id.Identifier;

    /// <summary>
    /// Converts a raw configuration key to a typed stub identifier.
    /// </summary>
    /// <param name="id">The raw configuration key.</param>
    public static implicit operator StubIdentifier(string id) => new StubIdentifier(id);

    /// <summary>
    /// Returns the raw identifier value.
    /// </summary>
    public override string ToString() => Identifier;
}
