using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TestFramework.Web.Auth;

/// <summary>
/// Applies authentication to an outgoing API request.
/// </summary>
/// <remarks>
/// Message-level modes (api key, bearer, basic) are applied here. Negotiate is a transport-level
/// concern and is configured on the sender's handler instead.
/// </remarks>
public interface IApiAuthenticationProvider
{
    /// <summary>
    /// Applies authentication values to the request message.
    /// </summary>
    /// <param name="message">The message about to be sent.</param>
    /// <param name="cancellationToken">The cancellation token for the running step.</param>
    Task ApplyAsync(HttpRequestMessage message, CancellationToken cancellationToken);
}
