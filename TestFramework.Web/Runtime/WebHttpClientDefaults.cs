using System;
using System.Net.Http;

namespace TestFramework.Web.Runtime;

/// <summary>
/// The transport settings every pooled client in this package is built with.
/// </summary>
/// <remarks>
/// One place, because the API sender and the stub admin client have the same problem: both are
/// pooled and both can outlive the endpoint they were built for.
/// </remarks>
internal static class WebHttpClientDefaults
{
    /// <summary>
    /// How many clients a pool keeps before it evicts and disposes the least recently used one.
    /// </summary>
    public const int PoolCapacity = 64;

    /// <summary>
    /// How long a pooled connection may be reused before it is replaced.
    /// </summary>
    /// <remarks>
    /// A client that lives across runs would otherwise never notice a DNS change: the name it
    /// resolved when it opened its first connection is the name it keeps using. Recycling the
    /// connection periodically is the documented way to get DNS back into the picture.
    /// </remarks>
    public static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long an idle connection is kept before it is closed.
    /// </summary>
    public static readonly TimeSpan ConnectionIdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates a handler with those settings.
    /// </summary>
    /// <param name="allowInvalidCertificates">Accepts any server certificate. For local hosts only.</param>
    /// <param name="useCookies">Keeps a cookie jar on the handler.</param>
    public static SocketsHttpHandler CreateHandler(bool allowInvalidCertificates, bool useCookies)
    {
        SocketsHttpHandler handler = new()
        {
            PooledConnectionLifetime = ConnectionLifetime,
            PooledConnectionIdleTimeout = ConnectionIdleTimeout,
            UseCookies = useCookies,
        };

        if (allowInvalidCertificates)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        return handler;
    }
}
