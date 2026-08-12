using System;
using System.Collections.Generic;

namespace TestFramework.Web.Http;

/// <summary>
/// Decides which header values may appear in logs and debugging output.
/// </summary>
public static class HttpHeaderRedaction
{
    private const string RedactedMarker = "(redacted)";

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "x-api-key",
        "api-key",
        "x-functions-key",
    };

    /// <summary>
    /// Registers an additional header name whose value must never be logged.
    /// </summary>
    /// <param name="name">The header name to treat as sensitive.</param>
    public static void AddSensitiveHeader(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (SensitiveHeaders)
        {
            SensitiveHeaders.Add(name);
        }
    }

    /// <summary>
    /// Returns the header value, or a redaction marker when the header is sensitive.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    public static string Redact(string name, string value)
    {
        lock (SensitiveHeaders)
        {
            return SensitiveHeaders.Contains(name) ? RedactedMarker : value;
        }
    }

    /// <summary>
    /// Returns a value indicating whether a header name is treated as sensitive.
    /// </summary>
    /// <param name="name">The header name.</param>
    public static bool IsSensitive(string name)
    {
        lock (SensitiveHeaders)
        {
            return SensitiveHeaders.Contains(name);
        }
    }
}
