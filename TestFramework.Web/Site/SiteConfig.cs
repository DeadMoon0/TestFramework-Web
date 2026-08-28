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
    /// Where it answers, when that is known yet.
    /// </summary>
    /// <remarks>
    /// Absent is a normal entry rather than an incomplete one - see <see cref="TestFramework.Web.Configuration.ApiConfig.BaseUrl"/>
    /// for the reasoning. A record rebuilt from a run always has one, because the shape refuses to rebuild
    /// without an address; only an entry read straight out of configuration may leave it open.
    /// </remarks>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// The address, for something about to use it.
    /// </summary>
    /// <remarks>
    /// The invariant stated once rather than asserted at each call site. A record rebuilt from a run always
    /// has an address - the shape refuses to rebuild without one - so this cannot fail for a config a step
    /// was handed; it can only fail for an entry somebody read straight out of configuration and then tried
    /// to connect with, which is the case that deserves a sentence rather than a null reference.
    /// </remarks>
    /// <exception cref="TestFramework.Core.Exceptions.FrameworkConfigurationException">The entry states no address.</exception>
    public string BaseUrlRequired
        => this.BaseUrl is { Length: > 0 } baseUrl
            ? baseUrl
            : throw new TestFramework.Core.Exceptions.FrameworkConfigurationException(
                "This entry states no address, so there is nothing to call.",
                ["Set its BaseUrl, or include the definition that provisions it so it publishes one when it starts."]);


    /// <summary>
    /// Accepts server certificates that fail validation. Intended for local hosts.
    /// </summary>
    public bool AllowInvalidCertificates { get; init; }
}
