using System;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Configuration;
using TestFramework.Web.Auth;
using TestFramework.Web.Exceptions;

namespace TestFramework.Web.Configuration;

/// <summary>
/// Default <see cref="IApiConfigProvider"/> implementation that reads API settings from the <c>Api</c> section.
/// </summary>
public class DefaultApiConfigProvider : IApiConfigProvider
{
    /// <summary>
    /// Configuration section name for <see cref="ApiConfig"/> records.
    /// </summary>
    public const string ApiSelector = "Api";

    /// <inheritdoc />
    public string[] LoadAllApiIdentifier(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return [.. configuration.GetSection(ApiSelector).GetChildren().Select(x => x.Key)];
    }

    /// <summary>
    /// Configuration section name holding module-wide web settings.
    /// </summary>
    public const string WebSelector = "Web";

    /// <summary>
    /// Configuration key holding additional header names to redact.
    /// </summary>
    public const string SensitiveHeadersSelector = "SensitiveHeaders";

    /// <inheritdoc />
    public WebRedactionOptions LoadRedactionOptions(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Accepts either a JSON array or a single comma-separated value, because both shapes turn up
        // in hand-written test settings.
        string[] configured =
        [
            .. configuration.GetSection(WebSelector).GetSection(SensitiveHeadersSelector)
                .GetChildren()
                .Select(child => child.Value)
                .Concat([configuration.GetSection(WebSelector).GetSection(SensitiveHeadersSelector).Value])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .SelectMany(value => value!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
        ];

        return configured.Length == 0 ? WebRedactionOptions.Default : WebRedactionOptions.Default.With(configured);
    }

    /// <inheritdoc />
    public ApiConfig LoadApiConfig(IConfiguration configuration, string identifier)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection section = configuration.GetSection(ApiSelector).GetSection(identifier);

        string baseUrl = section.GetSection(nameof(ApiConfig.BaseUrl)).Value
            ?? throw ApiConfigurationValidationException.InvalidValue(identifier, nameof(ApiConfig.BaseUrl), "the value is missing");

        ApiConfig config = new()
        {
            BaseUrl = baseUrl,
            Auth = ParseAuthMode(identifier, section.GetSection(nameof(ApiConfig.Auth)).Value),
            ApiKeyHeaderName = section.GetSection(nameof(ApiConfig.ApiKeyHeaderName)).Value,
            ApiKey = section.GetSection(nameof(ApiConfig.ApiKey)).Value,
            BearerToken = section.GetSection(nameof(ApiConfig.BearerToken)).Value,
            UserName = section.GetSection(nameof(ApiConfig.UserName)).Value,
            Password = section.GetSection(nameof(ApiConfig.Password)).Value,
            RequestTimeout = ParseTimeout(identifier, section.GetSection(nameof(ApiConfig.RequestTimeout)).Value),
            AllowInvalidCertificates = ParseBool(identifier, nameof(ApiConfig.AllowInvalidCertificates), section.GetSection(nameof(ApiConfig.AllowInvalidCertificates)).Value),
            UseCookies = ParseBool(identifier, nameof(ApiConfig.UseCookies), section.GetSection(nameof(ApiConfig.UseCookies)).Value),
        };

        string? healthPath = section.GetSection(nameof(ApiConfig.HealthPath)).Value;
        return string.IsNullOrWhiteSpace(healthPath) ? config : config with { HealthPath = healthPath };
    }

    private static ApiAuthMode ParseAuthMode(string identifier, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ApiAuthMode.None;

        if (Enum.TryParse(value, ignoreCase: true, out ApiAuthMode mode))
            return mode;

        throw ApiConfigurationValidationException.InvalidValue(
            identifier,
            nameof(ApiConfig.Auth),
            $"'{value}' is not a known mode. Use one of: {string.Join(", ", Enum.GetNames<ApiAuthMode>())}");
    }

    private static TimeSpan? ParseTimeout(string identifier, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsed) && parsed > TimeSpan.Zero)
            return parsed;

        throw ApiConfigurationValidationException.InvalidValue(
            identifier,
            nameof(ApiConfig.RequestTimeout),
            $"'{value}' is not a positive time span. Use a value such as '00:00:30'");
    }

    private static bool ParseBool(string identifier, string propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (bool.TryParse(value, out bool parsed))
            return parsed;

        throw ApiConfigurationValidationException.InvalidValue(identifier, propertyName, $"'{value}' is not a boolean");
    }
}
