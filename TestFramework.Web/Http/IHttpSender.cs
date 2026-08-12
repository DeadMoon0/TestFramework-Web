using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TestFramework.Web.Http;

/// <summary>
/// Sends API requests on behalf of a trigger.
/// </summary>
/// <remarks>
/// This is the seam that makes one timeline run unchanged across hosting modes: the default
/// implementation talks to a real endpoint, while a hosting environment can supply a sender bound
/// to an in-process test host. Triggers never construct an <see cref="HttpClient"/> themselves.
/// </remarks>
public interface IHttpSender
{
    /// <summary>
    /// Sends the request and returns the response.
    /// </summary>
    /// <param name="message">The request to send.</param>
    /// <param name="cancellationToken">The cancellation token for the running step.</param>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken);
}
