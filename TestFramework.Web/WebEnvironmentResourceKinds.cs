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
    /// Everything a REST API entry holds: where to reach it, and how a run talks to it.
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
    /// process dials is not what a peer container dials. Nothing else here does: a path is a path on whichever
    /// address you reached the API at, and a bearer token is the same token wherever you send it from.
    /// </para>
    /// <para>
    /// The whole entry, because a run's configuration is run data and travels on the run. Once it does, there
    /// is nothing left to classify per property - a value says whether it differs by viewpoint and whether it
    /// must never be printed, and both are declared here where the schema already lives.
    /// </para>
    /// </remarks>
    public static readonly ResourceKind RestApiKind = ResourceKind
        .Named(RestApi)
        .OffersPerVantage(ValueNames.BaseUrl)
        .Offers(nameof(Configuration.ApiConfig.HealthPath), ValueTraits.Optional)
        .Offers(nameof(Configuration.ApiConfig.Auth), ValueTraits.Optional)
        .Offers(nameof(Configuration.ApiConfig.ApiKeyHeaderName), ValueTraits.Optional)
        .Offers(nameof(Configuration.ApiConfig.RequestTimeout), ValueTraits.Optional)
        .Offers(nameof(Configuration.ApiConfig.AllowInvalidCertificates), ValueTraits.Optional)
        .Offers(nameof(Configuration.ApiConfig.UseCookies), ValueTraits.Optional)
        .Offers(nameof(Configuration.ApiConfig.UserName), ValueTraits.Optional)

        // The three that must never appear in a listing. A key or a token says who you are, and unlike a
        // connection string it is not also an address - so redacting it costs a reader nothing at all. The
        // user name is deliberately not among them: it identifies rather than authenticates, and a failure
        // that cannot say which account it tried is a worse failure.
        .Offers(nameof(Configuration.ApiConfig.ApiKey), ValueTraits.Optional | ValueTraits.Secret)
        .Offers(nameof(Configuration.ApiConfig.BearerToken), ValueTraits.Optional | ValueTraits.Secret)
        .Offers(nameof(Configuration.ApiConfig.Password), ValueTraits.Optional | ValueTraits.Secret)
        .Build();

    /// <summary>
    /// SQL database environment requirement kind.
    /// </summary>
    public const string Sql = "web.sql";

    /// <summary>
    /// What a SQL database resource offers: how to connect to it, what it is called, and how long a command
    /// may take.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three values for a ten-property record, and the reason is worth stating: a server, a user name, a
    /// password, integrated security, certificate trust and encryption are not six facts a reader wants -
    /// they are <em>ingredients of the connection string</em>, and this package's own factory composes them
    /// into one before it is published. What a reader needs is the string.
    /// </para>
    /// <para>
    /// That is also what keeps a rule the family learned the hard way, without anything having to enforce it:
    /// a connection string something started publishes is complete, and a declared user name cannot be
    /// layered on top of it, because a declared user name was never a value of its own. The credentials that
    /// apply are the ones inside whichever string won.
    /// </para>
    /// <para>
    /// The command timeout is the one property that is genuinely not in the string - it bounds a command
    /// rather than a connection - so it travels beside it.
    /// </para>
    /// <para>
    /// The connection string is secret and per viewpoint at once: it is the only value in the family that is
    /// an address and a credential in the same indivisible text, which is exactly why it has to travel and
    /// exactly why a listing must never print it.
    /// </para>
    /// </remarks>
    public static readonly ResourceKind SqlKind = ResourceKind
        .Named(Sql)
        .OffersPerVantage(ValueNames.ConnectionString, ValueTraits.Optional | ValueTraits.Secret)
        .Offers(DatabaseNameValue, ValueTraits.Optional)
        .Offers(CommandTimeoutValue, ValueTraits.Optional)
        .Build();

    /// <summary>
    /// The value name a SQL resource publishes its per-command timeout under.
    /// </summary>
    public const string CommandTimeoutValue = "CommandTimeout";

    /// <summary>
    /// The value name a SQL resource publishes its database name under.
    /// </summary>
    public const string DatabaseNameValue = "DatabaseName";

    /// <summary>
    /// Stubbed dependency environment requirement kind.
    /// </summary>
    public const string Stub = "web.stub";

    /// <summary>
    /// Everything a stub entry holds: where it is, and how a run behaves toward it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole record, not only its coordinates. A run's configuration is run data, so it travels on the
    /// run rather than in a store somebody resolves from a service provider - and once it does, there is no
    /// line left to draw between "a coordinate belongs here" and "a setting belongs there". A value says
    /// whether it differs by viewpoint and whether it must never be printed; nothing else needs deciding
    /// per property.
    /// </para>
    /// <para>
    /// Only the address differs by viewpoint. A path is a path on whichever address you reached the stub at,
    /// and how long a run is willing to poll does not change with where it is standing.
    /// </para>
    /// <para>
    /// All optional but the address, because a schema is an upper bound (§4): an entry holds what it got, and
    /// every one of these has a documented default on the record it rebuilds into.
    /// </para>
    /// </remarks>
    public static readonly ResourceKind StubKind = ResourceKind
        .Named(Stub)
        .OffersPerVantage(ValueNames.BaseUrl)
        .Offers(AdminPathValue, ValueTraits.Optional)
        .Offers(PollIntervalValue, ValueTraits.Optional)
        .Offers(AllowInvalidCertificatesValue, ValueTraits.Optional)
        .Offers(ResetModeValue, ValueTraits.Optional)
        .Build();

    /// <summary>
    /// The value name a stub resource publishes its admin path under.
    /// </summary>
    public const string AdminPathValue = "AdminPath";

    /// <summary>
    /// The value name a stub resource publishes its request-log poll interval under.
    /// </summary>
    public const string PollIntervalValue = "PollInterval";

    /// <summary>
    /// The value name a stub resource publishes its certificate tolerance under.
    /// </summary>
    public const string AllowInvalidCertificatesValue = "AllowInvalidCertificates";

    /// <summary>
    /// The value name a stub resource publishes its reset mode under.
    /// </summary>
    public const string ResetModeValue = "ResetMode";

    /// <summary>
    /// Web site environment requirement kind.
    /// </summary>
    public const string Site = "web.site";

    /// <summary>
    /// What a served site offers: where to reach it.
    /// </summary>
    /// <remarks>
    /// Where to reach it, per viewpoint, and whether its certificate may be untrusted. The second is here because a
    /// step reads the site back from the run's values with <c>Configured&lt;SiteConfig&gt;</c>, so everything the
    /// record holds has to travel through the run with it.
    /// </remarks>
    public static readonly ResourceKind SiteKind = ResourceKind
        .Named(Site)
        .OffersPerVantage(ValueNames.BaseUrl)
        .Offers(nameof(TestFramework.Web.Site.SiteConfig.AllowInvalidCertificates), ValueTraits.Optional)
        .Build();
}
