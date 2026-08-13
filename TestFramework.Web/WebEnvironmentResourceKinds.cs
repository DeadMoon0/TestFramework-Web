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
    /// SQL database environment requirement kind.
    /// </summary>
    public const string Sql = "web.sql";

    /// <summary>
    /// Stubbed dependency environment requirement kind.
    /// </summary>
    public const string Stub = "web.stub";
}
