using TestFramework.Core.Environment.Graph;
namespace TestFramework.Web;

/// <summary>
/// Canonical web resource kind names used for environment requirements.
/// </summary>
/// <remarks>
/// A step declares the kind it needs; the active environment provider decides how that kind is
/// satisfied. The same timeline therefore runs unchanged against a deployed API, an in-process
/// host, or a container.
/// </remarks>
public static class WebEnvironmentResourceKinds
{
    /// <summary>
    /// REST API environment requirement kind.
    /// </summary>
    public const string RestApi = "web.restapi";

    /// <summary>
    /// What a REST API resource offers the run: where to reach it, and where its health endpoint is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named after <see cref="RestApi"/> rather than repeating the string, because this and that must be the
    /// same resource. There were two kind vocabularies in this family for a while - these constants, used for
    /// environment requirements, and the graph's own kinds, used by nothing outside the engine - with names
    /// close enough that a reader could not tell they were unrelated. This is the one place they meet.
    /// </para>
    /// <para>
    /// The address is per viewpoint because the two really do differ once a container serves it: what the test
    /// process dials is not what a peer container dials. A health path is one value for everybody - it is a
    /// path, not a coordinate.
    /// </para>
    /// </remarks>
    public static readonly ResourceKind RestApiKind = ResourceKind
        .Named(RestApi)
        .OffersPerVantage(ValueNames.BaseUrl)
        .Offers(nameof(Configuration.ApiConfig.HealthPath), optional: true)
        .Build();

    /// <summary>
    /// SQL database environment requirement kind.
    /// </summary>
    public const string Sql = "web.sql";

    /// <summary>
    /// What a SQL database resource offers: how to connect to it, and what it is called.
    /// </summary>
    /// <remarks>
    /// The connection string is per viewpoint - a container's database answers on a mapped port to the test
    /// process and on its network alias to a peer - while the database's own name is one value, because a name
    /// is not a coordinate. Both are optional: an entry that names only a server and a database has no
    /// connection string of its own until something composes one, and a kind lists what an entry <em>may</em>
    /// hold rather than what it must.
    /// </remarks>
    public static readonly ResourceKind SqlKind = ResourceKind
        .Named(Sql)
        .OffersPerVantage(ValueNames.ConnectionString, optional: true)
        .Offers(DatabaseNameValue, optional: true)
        .Build();

    /// <summary>
    /// The value name a SQL resource publishes its database name under.
    /// </summary>
    public const string DatabaseNameValue = "DatabaseName";

    /// <summary>
    /// Stubbed dependency environment requirement kind.
    /// </summary>
    public const string Stub = "web.stub";

    /// <summary>
    /// What a stub server resource offers: where it is, and where its own control surface lives.
    /// </summary>
    /// <remarks>
    /// The admin path is one value for every viewpoint - it is a path on whichever address you reached the
    /// stub at, so publishing it twice would say something that is not true of it.
    /// </remarks>
    public static readonly ResourceKind StubKind = ResourceKind
        .Named(Stub)
        .OffersPerVantage(ValueNames.BaseUrl)
        .Offers(AdminPathValue, optional: true)
        .Build();

    /// <summary>
    /// The value name a stub resource publishes its admin path under.
    /// </summary>
    public const string AdminPathValue = "AdminPath";

    /// <summary>
    /// Web site environment requirement kind.
    /// </summary>
    public const string Site = "web.site";

    /// <summary>
    /// What a served site offers: where to reach it.
    /// </summary>
    /// <remarks>
    /// One value, and per viewpoint, because that is the whole of what a site is to the rest of a run. What a
    /// browser does about certificates is the browser's business rather than the site's, so it stays in the
    /// configuration record and out of the graph - a kind lists what a resource offers others, not everything
    /// its entry happens to say.
    /// </remarks>
    public static readonly ResourceKind SiteKind = ResourceKind
        .Named(Site)
        .OffersPerVantage(ValueNames.BaseUrl)
        .Build();
}
