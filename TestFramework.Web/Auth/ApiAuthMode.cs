namespace TestFramework.Web.Auth;

/// <summary>
/// Authentication mode applied to outgoing API requests.
/// </summary>
public enum ApiAuthMode
{
    /// <summary>
    /// No authentication is applied.
    /// </summary>
    None = 0,

    /// <summary>
    /// A static key is sent in the configured header.
    /// </summary>
    ApiKey = 1,

    /// <summary>
    /// A bearer token is sent in the <c>Authorization</c> header.
    /// </summary>
    Bearer = 2,

    /// <summary>
    /// A user name and password are sent as HTTP basic authentication.
    /// </summary>
    Basic = 3,

    /// <summary>
    /// The credentials of the current process are negotiated with the server.
    /// </summary>
    /// <remarks>
    /// This is the mode for APIs behind Windows integrated authentication. It requires no
    /// application change on the API side, but only works where the test process actually has
    /// usable Windows credentials.
    /// </remarks>
    Negotiate = 4,
}
