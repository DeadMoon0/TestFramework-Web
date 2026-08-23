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
    public void LoadSiteConfig_Throws_WhenBaseUrlIsMissing()
    {
        IConfiguration configuration = BuildConfiguration(("Site:shop:AllowInvalidCertificates", "true"));

        SiteConfigurationValidationException exception = Assert.Throws<SiteConfigurationValidationException>(
            () => new DefaultSiteConfigProvider().LoadSiteConfig(configuration, "shop"));

        Assert.Contains("BaseUrl", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Site:shop:BaseUrl", exception.Message, StringComparison.Ordinal);
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
    public void LoadWebSiteConfigs_RegistersTheStore_EvenWithoutASiteSection()
    {
        ServiceCollection services = new();

        services.LoadWebSiteConfigs(BuildConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();
        WebConfigStore<SiteConfig> store = Assert.IsType<WebConfigStore<SiteConfig>>(provider.GetService<WebConfigStore<SiteConfig>>());
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void LoadWebSiteConfigs_FillsTheStoreFromTheSection()
    {
        ServiceCollection services = new();

        services.LoadWebSiteConfigs(BuildConfiguration(("Site:shop:BaseUrl", "http://localhost:5090/")));

        using ServiceProvider provider = services.BuildServiceProvider();
        WebConfigStore<SiteConfig> store = provider.GetRequiredService<WebConfigStore<SiteConfig>>();
        Assert.Equal("http://localhost:5090/", store.GetConfig("shop").BaseUrl);
    }
}
