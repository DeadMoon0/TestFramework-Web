using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Configuration;
using TestFramework.Web.Extensions;
using TestFramework.Web.Http;

namespace TestFramework.Web.Tests;

/// <summary>
/// Covers the redaction policy: it comes from configuration rather than from code setup.
/// </summary>
public class WebSetupExtensionTests
{
    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    [Fact]
    public void SensitiveHeaders_AreReadFromAJsonArray()
    {
        IConfiguration configuration = BuildConfiguration(
            ("Web:SensitiveHeaders:0", "x-tenant-secret"),
            ("Web:SensitiveHeaders:1", "x-signature"));

        WebRedactionOptions options = new DefaultApiConfigProvider().LoadRedactionOptions(configuration);

        Assert.Contains("x-tenant-secret", options.AdditionalSensitiveHeaders);
        Assert.Contains("x-signature", options.AdditionalSensitiveHeaders);
    }

    [Fact]
    public void SensitiveHeaders_AreReadFromACommaSeparatedValue()
    {
        IConfiguration configuration = BuildConfiguration(("Web:SensitiveHeaders", "x-tenant-secret, x-signature"));

        WebRedactionOptions options = new DefaultApiConfigProvider().LoadRedactionOptions(configuration);

        Assert.Contains("x-tenant-secret", options.AdditionalSensitiveHeaders);
        Assert.Contains("x-signature", options.AdditionalSensitiveHeaders);
    }

    [Fact]
    public void SensitiveHeaders_FallBackToTheBuiltInPolicy_WhenNothingIsConfigured()
    {
        WebRedactionOptions options = new DefaultApiConfigProvider().LoadRedactionOptions(BuildConfiguration());

        Assert.Empty(options.AdditionalSensitiveHeaders);
        Assert.True(new HttpHeaderRedactor(options).IsSensitive("Authorization"));
    }

    [Fact]
    public void LoadWebConfigs_RegistersAConfiguredRedactor()
    {
        ServiceCollection services = new();
        services.LoadWebConfigs(BuildConfiguration(("Web:SensitiveHeaders:0", "x-tenant-secret")));

        HttpHeaderRedactor redactor = HttpHeaderRedactor.Resolve(services.BuildServiceProvider());

        Assert.True(redactor.IsSensitive("x-tenant-secret"));
        Assert.True(redactor.IsSensitive("Cookie"));
    }

    [Fact]
    public void Resolve_FallsBackToTheBuiltInPolicy_WhenNothingIsRegistered()
    {
        IServiceProvider provider = new ServiceCollection().BuildServiceProvider();

        Assert.Same(HttpHeaderRedactor.Default, HttpHeaderRedactor.Resolve(provider));
    }
}
