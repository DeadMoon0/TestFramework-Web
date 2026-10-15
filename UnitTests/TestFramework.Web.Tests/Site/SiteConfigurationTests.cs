using TestFramework.Config;
using TestFramework.Core.Steps;
using TestFramework.Core.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Configuration;
using TestFramework.Web.Extensions;
using TestFramework.Web.Site;
using TestFramework.Web.Site.Configuration;
using TestFramework.Web.Site.Exceptions;
using Xunit;

namespace TestFramework.Web.Tests.Site;

public class SiteConfigurationTests
{
    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    [Fact]
    public void LoadSiteConfig_ReadsEveryValue()
    {
        IConfiguration configuration = BuildConfiguration(
            ("Site:shop:BaseUrl", "http://localhost:5090/"),
            ("Site:shop:AllowInvalidCertificates", "true"));

        SiteConfig config = new DefaultSiteConfigProvider().LoadSiteConfig(configuration, "shop");

        Assert.Equal("http://localhost:5090/", config.BaseUrl);
        Assert.True(config.AllowInvalidCertificates);
    }

    [Fact]
    public void LoadSiteConfig_AppliesDefaults_WhenOnlyBaseUrlIsPresent()
    {
        IConfiguration configuration = BuildConfiguration(("Site:shop:BaseUrl", "http://localhost:5090/"));

        SiteConfig config = new DefaultSiteConfigProvider().LoadSiteConfig(configuration, "shop");

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
    public void LoadSiteConfig_AcceptsAnEntryWithNoAddress()
    {
        IConfiguration configuration = BuildConfiguration(("Site:shop:AllowInvalidCertificates", "true"));

        SiteConfig config = new DefaultSiteConfigProvider().LoadSiteConfig(configuration, "shop");

        Assert.Null(config.BaseUrl);
        Assert.True(config.AllowInvalidCertificates);

        FrameworkConfigurationException refused = Assert.Throws<FrameworkConfigurationException>(() => config.BaseUrlRequired);
        Assert.Contains("nothing to call", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadSiteConfig_Throws_WithActionableMessage_WhenABooleanCannotBeParsed()
    {
        IConfiguration configuration = BuildConfiguration(
            ("Site:shop:BaseUrl", "http://localhost:5090/"),
            ("Site:shop:AllowInvalidCertificates", "perhaps"));

        SiteConfigurationValidationException exception = Assert.Throws<SiteConfigurationValidationException>(
            () => new DefaultSiteConfigProvider().LoadSiteConfig(configuration, "shop"));

        Assert.Contains("perhaps", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Site:shop:AllowInvalidCertificates", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadAllSiteIdentifier_ListsEveryEntry()
    {
        IConfiguration configuration = BuildConfiguration(
            ("Site:shop:BaseUrl", "http://localhost:5090/"),
            ("Site:admin:BaseUrl", "http://localhost:5091/"));

        string[] identifiers = new DefaultSiteConfigProvider().LoadAllSiteIdentifier(configuration);

        Assert.Equal(["admin", "shop"], [.. identifiers.OrderBy(x => x, StringComparer.Ordinal)]);
    }

    [Fact]
    public void LoadWebSiteConfigs_LeavesARunAbleToAnswer_EvenWithoutASiteSection()
    {
        ServiceCollection services = new();
        IConfiguration configuration = BuildConfiguration();

        services.LoadWebSiteConfigs(configuration);
        services.AddSingleton(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();

        FrameworkConfigurationException exception = Assert.Throws<FrameworkConfigurationException>(
            () => RunContext.Detached(provider).Configured<SiteConfig>("shop"));

        Assert.Contains("Nothing in this run supplies", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadWebSiteConfigs_LetsARunReadTheSection()
    {
        ServiceCollection services = new();
        IConfiguration configuration = BuildConfiguration(("Site:shop:BaseUrl", "http://localhost:5090/"));

        services.LoadWebSiteConfigs(configuration);
        services.AddSingleton(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal("http://localhost:5090/", RunContext.Detached(provider).Configured<SiteConfig>("shop").BaseUrl);
    }
}
