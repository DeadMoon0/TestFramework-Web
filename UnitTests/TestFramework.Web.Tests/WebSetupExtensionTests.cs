using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config;
using TestFramework.Web.Configuration;
using TestFramework.Web.Extensions;
using TestFramework.Web.Http;
using TestFramework.Web.Trigger;

namespace TestFramework.Web.Tests;

/// <summary>
/// Covers the setup surface: redaction is configuration, and trigger behaviour has its own
/// extension method rather than a raw service registration.
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

    [Fact]
    public void ConfigureApiTrigger_ProjectsOntoTheDefaults()
    {
        IServiceProvider provider = ConfigInstance.Create()
            .ConfigureApiTrigger(config => config with { LogRequestHeaders = true, LogRequests = false })
            .Build()
            .BuildServiceProvider();

        ApiTriggerConfig config = provider.GetRequiredService<ApiTriggerConfig>();

        Assert.True(config.LogRequestHeaders);
        Assert.False(config.LogRequests);

        // Untouched values keep their defaults, so callers only state what they are changing.
        Assert.Equal(TimeSpan.FromSeconds(10), config.LocalWarmupRetryDuration);
    }

    [Fact]
    public void RedactHeaders_AddsNamesFromCode()
    {
        IServiceProvider provider = ConfigInstance.Create()
            .RedactHeaders("x-runtime-secret")
            .Build()
            .BuildServiceProvider();

        Assert.True(HttpHeaderRedactor.Resolve(provider).IsSensitive("x-runtime-secret"));
    }
}
