using System;
using System.Collections.Generic;
using System.Linq;

namespace TestFramework.Web.Configuration;

/// <summary>
/// Names of headers whose values must never reach a log or a debug value.
/// </summary>
/// <remarks>
/// This is configuration, not code setup: put the header names in the <c>Web:SensitiveHeaders</c>
/// section so the policy travels with the test settings instead of being re-registered by every
/// test project. The built-in names always apply on top of whatever is configured.
/// </remarks>
public sealed record WebRedactionOptions
{
    /// <summary>
    /// Header names that are always redacted, whatever the configuration says.
    /// </summary>
    public static IReadOnlyCollection<string> BuiltInSensitiveHeaders { get; } =
    [
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "x-api-key",
        "api-key",
        "x-functions-key",
    ];

    /// <summary>
    /// Gets the options containing only the built-in header names.
    /// </summary>
    public static WebRedactionOptions Default { get; } = new();

    /// <summary>
    /// Additional header names to redact, usually loaded from <c>Web:SensitiveHeaders</c>.
    /// </summary>
    public IReadOnlyCollection<string> AdditionalSensitiveHeaders { get; init; } = [];

    /// <summary>
    /// Returns options extended with further header names.
    /// </summary>
    /// <param name="headerNames">The header names to add.</param>
    public WebRedactionOptions With(params string[] headerNames)
    {
        ArgumentNullException.ThrowIfNull(headerNames);
        return this with { AdditionalSensitiveHeaders = [.. AdditionalSensitiveHeaders, .. headerNames] };
    }

    internal HashSet<string> BuildHeaderSet()
        => new(BuiltInSensitiveHeaders.Concat(AdditionalSensitiveHeaders), StringComparer.OrdinalIgnoreCase);
}
