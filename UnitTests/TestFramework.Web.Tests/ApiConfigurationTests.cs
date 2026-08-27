using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Auth;
using TestFramework.Core.Steps;
using TestFramework.Web.Configuration;
using TestFramework.Web.Exceptions;
using TestFramework.Web.Extensions;
using Xunit;

namespace TestFramework.Web.Tests;

public class ApiConfigurationTests
{
    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    [Fact]
    public void LoadApiConfig_ReadsEveryValue()
    {
        IConfiguration configuration = BuildConfiguration(
            ("Api:orders:BaseUrl", "http://localhost:5080/"),
            ("Api:orders:HealthPath", "/status"),
            ("Api:orders:Auth", "Bearer"),
            ("Api:orders:BearerToken", "token-value"),
            ("Api:orders:RequestTimeout", "00:00:45"),
            ("Api:orders:AllowInvalidCertificates", "true"));

        ApiConfig config = new DefaultApiConfigProvider().LoadApiConfig(configuration, "orders");

        Assert.Equal("http://localhost:5080/", config.BaseUrl);
        Assert.Equal("/status", config.HealthPath);
        Assert.Equal(ApiAuthMode.Bearer, config.Auth);
        Assert.Equal("token-value", config.BearerToken);
        Assert.Equal(TimeSpan.FromSeconds(45), config.RequestTimeout);
        Assert.True(config.AllowInvalidCertificates);
    }

    [Fact]
    public void LoadApiConfig_AppliesDefaults_WhenOnlyBaseUrlIsPresent()
    {
        IConfiguration configuration = BuildConfiguration(("Api:orders:BaseUrl", "http://localhost:5080/"));

        ApiConfig config = new DefaultApiConfigProvider().LoadApiConfig(configuration, "orders");

        Assert.Equal("/health", config.HealthPath);
        Assert.Equal(ApiAuthMode.None, config.Auth);
        Assert.Null(config.RequestTimeout);
        Assert.False(config.AllowInvalidCertificates);
    }

    [Fact]
    public void LoadApiConfig_Throws_WhenBaseUrlIsMissing()
    {
        IConfiguration configuration = BuildConfiguration(("Api:orders:Auth", "None"));

        ApiConfigurationValidationException exception = Assert.Throws<ApiConfigurationValidationException>(
            () => new DefaultApiConfigProvider().LoadApiConfig(configuration, "orders"));

        Assert.Contains("BaseUrl", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Api:orders:BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Api:orders:Auth", "Sideways")]
    [InlineData("Api:orders:RequestTimeout", "soon")]
    [InlineData("Api:orders:AllowInvalidCertificates", "perhaps")]
    public void LoadApiConfig_Throws_WithActionableMessage_WhenAValueCannotBeParsed(string key, string value)
    {
        IConfiguration configuration = BuildConfiguration(("Api:orders:BaseUrl", "http://localhost:5080/"), (key, value));

        ApiConfigurationValidationException exception = Assert.Throws<ApiConfigurationValidationException>(
            () => new DefaultApiConfigProvider().LoadApiConfig(configuration, "orders"));

        Assert.Contains(value, exception.Message, StringComparison.Ordinal);
        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadAllApiIdentifier_ReturnsEveryConfiguredIdentifier()
    {
        IConfiguration configuration = BuildConfiguration(
            ("Api:orders:BaseUrl", "http://localhost:1/"),
            ("Api:invoices:BaseUrl", "http://localhost:2/"));

        string[] identifiers = new DefaultApiConfigProvider().LoadAllApiIdentifier(configuration);

        Assert.Equal(["invoices", "orders"], identifiers.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void LoadWebConfigs_RegistersTheStore_EvenWithoutAnApiSection()
    {
        ServiceCollection services = new();
        services.LoadWebConfigs(BuildConfiguration());

        WebConfigStore<ApiConfig> store = services.BuildServiceProvider().GetRequiredService<WebConfigStore<ApiConfig>>();

        // An environment hydrates identifiers at run time, so the store must exist even when the
        // static configuration declares nothing.
        Assert.Empty(store.Snapshot());
    }

    // The two resolution cases that used to live here have moved to ApiResourceTests, and could not stay.
    // They resolved against RunContext.Detached, which has no resources by construction - and configuration
    // is read from the run now, so a test about resolving it needs a run. What replaced them covers more:
    // a declaration answering alone, a container's address winning over it, an entry nobody wrote, and a
    // refusal naming what does exist.
    //
    // One behaviour changed with them, and it is public. "No configuration for this identifier" used to be
    // an ApiConfigurationValidationException from this package; it is now a FrameworkConfigurationException
    // from the one reader the whole family shares. Both derive from TimelineFrameworkException, so a caller
    // catching that still catches it - one catching the specific type no longer does.
}
