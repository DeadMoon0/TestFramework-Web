using TestFramework.Core.Conventions;

namespace TestFramework.Web.Site;

/// <summary>
/// Logical identifier used to resolve a configured web site.
/// </summary>
/// <remarks>
/// The site is the application a browser loads, so browser steps and the site share one identifier:
/// a UI test that drives <c>"shop"</c> is served by the site declared as <c>"shop"</c>.
/// </remarks>
/// <param name="Identifier">The configuration key that names the site entry.</param>
[ClosedShape("An identifier is its name; anything more about a site belongs in its configuration.")]
public sealed record SiteIdentifier(string Identifier)
{
    /// <summary>
    /// Converts a typed identifier to its raw string representation.
    /// </summary>
    /// <param name="id">The typed identifier instance.</param>
    public static implicit operator string(SiteIdentifier id) => id.Identifier;

    /// <summary>
    /// Converts a raw configuration key to a typed site identifier.
    /// </summary>
    /// <param name="id">The raw configuration key.</param>
    public static implicit operator SiteIdentifier(string id) => new SiteIdentifier(id);

    /// <summary>
    /// Returns the raw identifier value.
    /// </summary>
    public override string ToString() => Identifier;
}
