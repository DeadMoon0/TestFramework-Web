namespace TestFramework.Web.Site;

/// <summary>
/// Configuration required to reach a web site with a browser.
/// </summary>
/// <remarks>
/// A site is reached like any other web resource: by identifier. Whether it is served by a container
/// this run started, or was already deployed somewhere, is not visible to the timeline.
/// </remarks>
public record SiteConfig
{
    /// <summary>
    /// Absolute address the site answers on, for example <c>http://localhost:32780/</c>.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Accepts server certificates that fail validation. Intended for local hosts.
    /// </summary>
    public bool AllowInvalidCertificates { get; init; }
}
