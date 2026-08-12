using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Configuration;

namespace TestFramework.Web.Http;

/// <summary>
/// Decides which header values may appear in logs and debugging output.
/// </summary>
/// <remarks>
/// An instance is built from <see cref="WebRedactionOptions"/> and resolved from the run's services,
/// so the policy is configuration rather than global mutable state that leaks between tests.
/// </remarks>
public sealed class HttpHeaderRedactor
{
    /// <summary>
    /// Marker substituted for a sensitive header value.
    /// </summary>
    public const string RedactedMarker = "(redacted)";

    private readonly HashSet<string> _sensitiveHeaders;

    /// <summary>
    /// Creates a redactor for the provided options.
    /// </summary>
    /// <param name="options">The header names to treat as sensitive.</param>
    public HttpHeaderRedactor(WebRedactionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _sensitiveHeaders = options.BuildHeaderSet();
    }

    /// <summary>
    /// Gets a redactor that applies only the built-in header names.
    /// </summary>
    public static HttpHeaderRedactor Default { get; } = new(WebRedactionOptions.Default);

    /// <summary>
    /// Resolves the redactor configured for the current run, falling back to the built-in policy.
    /// </summary>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    public static HttpHeaderRedactor Resolve(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (serviceProvider.GetService<HttpHeaderRedactor>() is { } registered)
            return registered;

        return serviceProvider.GetService<WebRedactionOptions>() is { } options
            ? new HttpHeaderRedactor(options)
            : Default;
    }

    /// <summary>
    /// Returns a value indicating whether a header name is treated as sensitive.
    /// </summary>
    /// <param name="name">The header name.</param>
    public bool IsSensitive(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _sensitiveHeaders.Contains(name);
    }

    /// <summary>
    /// Returns the header value, or the redaction marker when the header is sensitive.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    public string Redact(string name, string value) => IsSensitive(name) ? RedactedMarker : value;

    /// <summary>
    /// Renders header pairs as a single log-safe line.
    /// </summary>
    /// <param name="headers">The headers to render.</param>
    public string Describe(IEnumerable<KeyValuePair<string, string[]>> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return string.Join(", ", headers.Select(header => IsSensitive(header.Key)
            ? $"{header.Key}={RedactedMarker}"
            : $"{header.Key}={string.Join('|', header.Value)}"));
    }
}
