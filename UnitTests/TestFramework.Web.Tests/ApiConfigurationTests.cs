using TestFramework.Config;
using TestFramework.Core.Exceptions;
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
    /// <summary>
    /// An entry may state everything except where the thing is.
    /// </summary>
    /// <remarks>
    /// The reversal of a load-time refusal, and deliberate: a resource a container is about to start has no
    /// address to state, so demanding one forced a placeholder nobody dials. What is not relaxed is using it
    /// - the shape refuses to rebuild a record without an address, so the failure lands where something is
    /// about to connect rather than where somebody wrote a file.
    /// </remarks>
    public void LoadApiConfig_AcceptsAnEntryWithNoAddress()
    {
        IConfiguration configuration = BuildConfiguration(("Api:orders:Auth", "None"));

        ApiConfig config = new DefaultApiConfigProvider().LoadApiConfig(configuration, "orders");

        Assert.Null(config.BaseUrl);
        Assert.Equal(ApiAuthMode.None, config.Auth);

        // Using it is still refused, and says both ways to supply one.
        FrameworkConfigurationException refused = Assert.Throws<FrameworkConfigurationException>(() => config.BaseUrlRequired);
        Assert.Contains("nothing to call", refused.Message, StringComparison.Ordinal);
        Assert.Contains("publishes one when it starts", refused.ToString(), StringComparison.Ordinal);
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

    /// <summary>
    /// A missing section reads as "nothing is configured", not as "this package is not loaded".
    /// </summary>
    /// <remarks>
    /// This used to assert that an empty store was registered, because an environment could hydrate it later.
    /// Nothing hydrates a store any more, so what matters is that the package is still able to answer - and
    /// that the answer names the section rather than the package.
    /// </remarks>
    [Fact]
    public void AddWeb_LeavesARunAbleToAnswer_EvenWithoutAnApiSection()
    {
        ServiceCollection services = new();
        services.AddWeb(BuildConfiguration());
        services.AddSingleton(BuildConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();

        FrameworkConfigurationException exception = Assert.Throws<FrameworkConfigurationException>(
            () => RunContext.Detached(provider).Configured<ApiConfig>("orders"));

        Assert.Contains("Nothing in this run supplies", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Api:orders", exception.ToString(), StringComparison.Ordinal);
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
