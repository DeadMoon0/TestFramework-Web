using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TestFramework.Web.Auth;

/// <summary>
/// Applies authentication through a caller-supplied delegate.
/// </summary>
/// <remarks>
/// Use this for token flows the framework does not model, for example fetching a token from an
/// identity provider before the request is sent.
/// </remarks>
public sealed class DelegateApiAuthenticationProvider : IApiAuthenticationProvider
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task> _apply;

    /// <summary>
    /// Creates a provider from an asynchronous delegate.
    /// </summary>
    /// <param name="apply">The delegate that mutates the outgoing message.</param>
    public DelegateApiAuthenticationProvider(Func<HttpRequestMessage, CancellationToken, Task> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        _apply = apply;
    }

    /// <summary>
    /// Creates a provider from a synchronous delegate.
    /// </summary>
    /// <param name="apply">The delegate that mutates the outgoing message.</param>
    public DelegateApiAuthenticationProvider(Action<HttpRequestMessage> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        _apply = (message, _) =>
        {
            apply(message);
            return Task.CompletedTask;
        };
    }

    /// <inheritdoc />
    public Task ApplyAsync(HttpRequestMessage message, CancellationToken cancellationToken) => _apply(message, cancellationToken);
}
