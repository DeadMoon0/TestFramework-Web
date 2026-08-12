using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Web.Auth;
using TestFramework.Web.Configuration;
using TestFramework.Web.Exceptions;
using TestFramework.Web.Http;
using Xunit;

namespace TestFramework.Web.Tests;

public class AuthenticationTests
{
    private static ApiConfig Config(ApiAuthMode mode) => new()
    {
        BaseUrl = "http://localhost:5080/",
        Auth = mode,
    };

    [Fact]
    public async Task ApiKeyMode_SendsTheConfiguredHeader()
    {
        ApiConfig config = Config(ApiAuthMode.ApiKey) with { ApiKeyHeaderName = "x-api-key", ApiKey = "secret" };
        using HttpRequestMessage message = new(HttpMethod.Get, "http://localhost:5080/");

        await new ConfiguredApiAuthenticationProvider("sample", config).ApplyAsync(message, CancellationToken.None);

        Assert.Equal(["secret"], message.Headers.GetValues("x-api-key"));
    }

    [Fact]
    public async Task BearerMode_SendsAnAuthorizationHeader()
    {
        ApiConfig config = Config(ApiAuthMode.Bearer) with { BearerToken = "token" };
        using HttpRequestMessage message = new(HttpMethod.Get, "http://localhost:5080/");

        await new ConfiguredApiAuthenticationProvider("sample", config).ApplyAsync(message, CancellationToken.None);

        Assert.Equal("Bearer", message.Headers.Authorization?.Scheme);
        Assert.Equal("token", message.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task BasicMode_SendsBase64Credentials()
    {
        ApiConfig config = Config(ApiAuthMode.Basic) with { UserName = "user", Password = "pass" };
        using HttpRequestMessage message = new(HttpMethod.Get, "http://localhost:5080/");

        await new ConfiguredApiAuthenticationProvider("sample", config).ApplyAsync(message, CancellationToken.None);

        Assert.Equal("Basic", message.Headers.Authorization?.Scheme);
        Assert.Equal("dXNlcjpwYXNz", message.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task NoneMode_LeavesTheMessageUntouched()
    {
        using HttpRequestMessage message = new(HttpMethod.Get, "http://localhost:5080/");

        await new ConfiguredApiAuthenticationProvider("sample", Config(ApiAuthMode.None)).ApplyAsync(message, CancellationToken.None);

        Assert.Null(message.Headers.Authorization);
        Assert.Empty(message.Headers);
    }

    [Fact]
    public async Task NegotiateMode_AddsNoMessageHeaders()
    {
        // Negotiate is a transport concern: the handler carries the credentials, not the message.
        using HttpRequestMessage message = new(HttpMethod.Get, "http://localhost:5080/");

        await new ConfiguredApiAuthenticationProvider("sample", Config(ApiAuthMode.Negotiate)).ApplyAsync(message, CancellationToken.None);

        Assert.Null(message.Headers.Authorization);
    }

    [Theory]
    [InlineData(ApiAuthMode.ApiKey, "ApiKeyHeaderName")]
    [InlineData(ApiAuthMode.Bearer, "BearerToken")]
    [InlineData(ApiAuthMode.Basic, "UserName")]
    public async Task IncompleteAuth_ThrowsWithTheMissingKeyNamed(ApiAuthMode mode, string expectedProperty)
    {
        using HttpRequestMessage message = new(HttpMethod.Get, "http://localhost:5080/");
        ConfiguredApiAuthenticationProvider provider = new("sample", Config(mode));

        ApiConfigurationValidationException exception = await Assert.ThrowsAsync<ApiConfigurationValidationException>(
            () => provider.ApplyAsync(message, CancellationToken.None));

        Assert.Contains(expectedProperty, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Api:sample:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DelegateProvider_AppliesTheDelegate()
    {
        using HttpRequestMessage message = new(HttpMethod.Get, "http://localhost:5080/");
        DelegateApiAuthenticationProvider provider = new(m => m.Headers.TryAddWithoutValidation("x-custom", "value"));

        await provider.ApplyAsync(message, CancellationToken.None);

        Assert.Equal(["value"], message.Headers.GetValues("x-custom"));
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    [InlineData("x-api-key")]
    [InlineData("Cookie")]
    public void BuiltInSensitiveHeaders_AreAlwaysRedacted(string headerName)
    {
        Assert.True(HttpHeaderRedactor.Default.IsSensitive(headerName));
        Assert.Equal(HttpHeaderRedactor.RedactedMarker, HttpHeaderRedactor.Default.Redact(headerName, "secret"));
    }

    [Fact]
    public void OrdinaryHeaders_AreNotRedacted()
    {
        Assert.False(HttpHeaderRedactor.Default.IsSensitive("x-correlation-id"));
        Assert.Equal("abc", HttpHeaderRedactor.Default.Redact("x-correlation-id", "abc"));
    }

    [Fact]
    public void ConfiguredSensitiveHeaders_AreRedactedOnTopOfTheBuiltInNames()
    {
        HttpHeaderRedactor redactor = new(WebRedactionOptions.Default.With("x-tenant-secret"));

        Assert.True(redactor.IsSensitive("x-tenant-secret"));
        Assert.True(redactor.IsSensitive("Authorization"));
    }

    [Fact]
    public void Describe_RendersHeadersWithSensitiveValuesHidden()
    {
        HttpHeaderRedactor redactor = HttpHeaderRedactor.Default;

        string described = redactor.Describe(
        [
            new KeyValuePair<string, string[]>("Authorization", ["Bearer super-secret"]),
            new KeyValuePair<string, string[]>("x-correlation-id", ["corr-1"]),
        ]);

        Assert.DoesNotContain("super-secret", described, StringComparison.Ordinal);
        Assert.Contains("corr-1", described, StringComparison.Ordinal);
    }
}
