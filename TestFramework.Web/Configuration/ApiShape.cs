using System;
using System.Collections.Generic;
using TestFramework.Web.Auth;
using TestFramework.Core.Exceptions;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using TestFramework.Config.Configuration;
using TestFramework.Core.Environment.Graph;

namespace TestFramework.Web.Configuration;

/// <summary>
/// The <c>Api</c> section, as the run's resource graph sees it.
/// </summary>
/// <remarks>
/// <para>
/// A configured REST API is a resource somebody declared, and this is what says so: which section it lives
/// in, which kind of resource an entry describes, and which of the graph's values the entry actually holds.
/// The point is what it removes. A step that needs an address used to have to know whether the answer came
/// from a file or from a container that had just started, and ask the right one; with the entry in the graph
/// it asks the run once and never learns who answered.
/// </para>
/// <para>
/// It reads by delegating to the same provider the package's own loader uses, rather than binding the section
/// again here. Two readers for one section is two sets of validation rules, and the second one written is the
/// one that quietly accepts what the first refuses.
/// </para>
/// </remarks>
/// <param name="provider">Reads an entry, including whatever validation the caller configured.</param>
internal sealed class ApiShape(IApiConfigProvider provider) : ConfigShape<ApiConfig>
{
    /// <inheritdoc />
    public override string Section => DefaultApiConfigProvider.ApiSelector;

    /// <inheritdoc />
    public override ResourceKind Kind => WebEnvironmentResourceKinds.RestApiKind;

    /// <inheritdoc />
    public override IReadOnlyList<string> Identifiers(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadAllApiIdentifier(configuration);
    }

    /// <inheritdoc />
    public override ApiConfig Read(IConfiguration configuration, string identifier)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return provider.LoadApiConfig(configuration, identifier);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Whatever the entry holds, all of it. An address a person wrote is the same from every viewpoint - they
    /// wrote the one address the thing answers on - so it is published for both rather than for the host
    /// alone, which would leave a container-side reader with nothing. A container that later serves the same
    /// identifier produces two genuinely different addresses and overrides these.
    /// </para>
    /// <para>
    /// The credentials go in as well, and the kind is what says three of them must never be printed. They are
    /// still real values to whatever connects; what changes is that the listing a failure message is built
    /// from redacts them by construction rather than by whoever formats it remembering to.
    /// </para>
    /// </remarks>
    public override IReadOnlyDictionary<ValueKey, string> Values(ApiConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Dictionary<ValueKey, string> values = [];

        if (config.BaseUrl is { Length: > 0 } baseUrl)
        {
            values[new ValueKey(ValueNames.BaseUrl, ResourceVantage.Host)] = baseUrl;
            values[new ValueKey(ValueNames.BaseUrl, ResourceVantage.Network)] = baseUrl;
        }

        if (config.HealthPath is { Length: > 0 } healthPath)
        {
            values[new ValueKey(nameof(ApiConfig.HealthPath))] = healthPath;
        }

        // Round-trip formats: an enum by name, a flag as "true"/"false", a duration as ISO-8601, so what
        // comes back is what went in whatever culture the machine runs in.
        values[new ValueKey(nameof(ApiConfig.Auth))] = config.Auth.ToString();
        values[new ValueKey(nameof(ApiConfig.AllowInvalidCertificates))] = config.AllowInvalidCertificates ? "true" : "false";
        values[new ValueKey(nameof(ApiConfig.UseCookies))] = config.UseCookies ? "true" : "false";

        if (config.RequestTimeout is { } requestTimeout)
        {
            values[new ValueKey(nameof(ApiConfig.RequestTimeout))] = requestTimeout.ToString("c", CultureInfo.InvariantCulture);
        }

        Add(values, nameof(ApiConfig.ApiKeyHeaderName), config.ApiKeyHeaderName);
        Add(values, nameof(ApiConfig.UserName), config.UserName);
        Add(values, nameof(ApiConfig.ApiKey), config.ApiKey);
        Add(values, nameof(ApiConfig.BearerToken), config.BearerToken);
        Add(values, nameof(ApiConfig.Password), config.Password);

        return values;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Absent values leave the record's own defaults, which is what keeps §5 true: a default is documented
    /// where the value is introduced and this does not restate it. A value that is present and unreadable is
    /// refused rather than quietly defaulted - something put it there deliberately.
    /// </para>
    /// <para>
    /// Nothing here reaches for configuration again. By the time this runs a container may have overridden
    /// half of the entry, and reading the file would silently undo that.
    /// </para>
    /// </remarks>
    public override ApiConfig Read(IReadOnlyDictionary<string, string> values, string identifier)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (!values.TryGetValue(ValueNames.BaseUrl, out string? baseUrl) || string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new FrameworkConfigurationException(
                $"The run has no address for API '{identifier}'.",
                [
                    $"Set 'Api:{identifier}:BaseUrl', or include the definition that provisions it.",
                ],
                [.. values.Keys]);
        }

        ApiConfig config = new ApiConfig
        {
            BaseUrl = baseUrl,
            ApiKeyHeaderName = Text(values, nameof(ApiConfig.ApiKeyHeaderName)),
            ApiKey = Text(values, nameof(ApiConfig.ApiKey)),
            BearerToken = Text(values, nameof(ApiConfig.BearerToken)),
            UserName = Text(values, nameof(ApiConfig.UserName)),
            Password = Text(values, nameof(ApiConfig.Password)),
        };

        if (Text(values, nameof(ApiConfig.HealthPath)) is { Length: > 0 } healthPath)
        {
            config = config with { HealthPath = healthPath };
        }

        if (values.TryGetValue(nameof(ApiConfig.Auth), out string? auth))
        {
            config = config with
            {
                Auth = Enum.TryParse(auth, ignoreCase: true, out ApiAuthMode parsed)
                    ? parsed
                    : throw Unreadable(auth, identifier, nameof(ApiConfig.Auth), string.Join(" or ", Enum.GetNames<ApiAuthMode>())),
            };
        }

        if (values.TryGetValue(nameof(ApiConfig.AllowInvalidCertificates), out string? allowInvalid))
        {
            config = config with { AllowInvalidCertificates = Flag(allowInvalid, identifier, nameof(ApiConfig.AllowInvalidCertificates)) };
        }

        if (values.TryGetValue(nameof(ApiConfig.UseCookies), out string? useCookies))
        {
            config = config with { UseCookies = Flag(useCookies, identifier, nameof(ApiConfig.UseCookies)) };
        }

        if (values.TryGetValue(nameof(ApiConfig.RequestTimeout), out string? requestTimeout))
        {
            config = config with
            {
                RequestTimeout = TimeSpan.TryParse(requestTimeout, CultureInfo.InvariantCulture, out TimeSpan parsed)
                    ? parsed
                    : throw Unreadable(requestTimeout, identifier, nameof(ApiConfig.RequestTimeout), "a duration such as '00:00:30'"),
            };
        }

        return config;
    }

    private static void Add(Dictionary<ValueKey, string> values, string valueName, string? value)
    {
        if (value is { Length: > 0 })
        {
            values[new ValueKey(valueName)] = value;
        }
    }

    private static string? Text(IReadOnlyDictionary<string, string> values, string valueName)
        => values.TryGetValue(valueName, out string? value) && value is { Length: > 0 } ? value : null;

    private static bool Flag(string value, string identifier, string valueName)
        => bool.TryParse(value, out bool parsed)
            ? parsed
            : throw Unreadable(value, identifier, valueName, "'true' or 'false'");

    /// <summary>
    /// The failure for a value that is present and cannot be read.
    /// </summary>
    /// <remarks>
    /// It names the value and what was expected, and never the value's content beyond what was handed in -
    /// which for a secret is the reason nothing unparseable is echoed from one.
    /// </remarks>
    private static FrameworkConfigurationException Unreadable(string value, string identifier, string valueName, string expected)
        => new FrameworkConfigurationException(
            $"The run holds '{value}' as {valueName} for API '{identifier}', which cannot be read as {expected}.",
            [
                $"Correct 'Api:{identifier}:{valueName}', or the environment component that publishes it.",
            ]);
}
