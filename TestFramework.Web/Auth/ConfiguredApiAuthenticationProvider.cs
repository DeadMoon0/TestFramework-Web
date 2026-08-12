using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Web.Configuration;
using TestFramework.Web.Exceptions;

namespace TestFramework.Web.Auth;

/// <summary>
/// Applies the authentication mode declared in an <see cref="ApiConfig"/>.
/// </summary>
/// <param name="identifier">The API identifier the configuration belongs to.</param>
/// <param name="config">The configuration describing the authentication mode.</param>
public sealed class ConfiguredApiAuthenticationProvider(string identifier, ApiConfig config) : IApiAuthenticationProvider
{
    /// <inheritdoc />
    public Task ApplyAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        switch (config.Auth)
        {
            case ApiAuthMode.ApiKey:
                if (string.IsNullOrWhiteSpace(config.ApiKeyHeaderName) || string.IsNullOrWhiteSpace(config.ApiKey))
                    throw ApiConfigurationValidationException.IncompleteAuth(identifier, nameof(ApiAuthMode.ApiKey), nameof(ApiConfig.ApiKeyHeaderName), nameof(ApiConfig.ApiKey));

                message.Headers.TryAddWithoutValidation(config.ApiKeyHeaderName, config.ApiKey);
                break;

            case ApiAuthMode.Bearer:
                if (string.IsNullOrWhiteSpace(config.BearerToken))
                    throw ApiConfigurationValidationException.IncompleteAuth(identifier, nameof(ApiAuthMode.Bearer), nameof(ApiConfig.BearerToken));

                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.BearerToken);
                break;

            case ApiAuthMode.Basic:
                if (string.IsNullOrWhiteSpace(config.UserName))
                    throw ApiConfigurationValidationException.IncompleteAuth(identifier, nameof(ApiAuthMode.Basic), nameof(ApiConfig.UserName), nameof(ApiConfig.Password));

                string raw = $"{config.UserName}:{config.Password}";
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
                break;

            case ApiAuthMode.Negotiate:
                // Handled on the sender's handler through default credentials.
                break;

            case ApiAuthMode.None:
            default:
                break;
        }

        return Task.CompletedTask;
    }
}
