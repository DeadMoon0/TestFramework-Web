using System;
using System.Collections.Generic;
using TestFramework.Config;
using TestFramework.Config.Builder.InstanceBuilder;
using TestFramework.Web.Extensions;
using TestFramework.Web.Http;

namespace TestFramework.Web.Tests;

/// <summary>
/// Pins that every way of declaring a header secret counts, whichever came last.
/// </summary>
/// <remarks>
/// The damage it prevents is a secret header logged in clear. <c>Web:SensitiveHeaders</c> and <c>.RedactHeaders(...)</c>
/// each registered a redactor of their own, and the run used whichever was registered last - so the README's own
/// recommendation, configuration first and one extra header in code, dropped the configured ones.
/// </remarks>
public sealed class RedactionDeclarationTests
{
    [Fact]
    public void ConfiguredHeadersSurviveAHeaderAddedInCode()
    {
        HttpHeaderRedactor redactor = RedactorOf(builder => builder
            .OverrideConfig(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Web:SensitiveHeaders:0"] = "x-configured-secret" })
            .LoadWebConfig()
            .RedactHeaders("x-code-secret"));

        Assert.True(redactor.IsSensitive("x-configured-secret"));
        Assert.True(redactor.IsSensitive("x-code-secret"));
        Assert.True(redactor.IsSensitive("Authorization"));
    }

    [Fact]
    public void AHeaderAddedInCodeSurvivesTheConfigurationLoadedAfterIt()
    {
        HttpHeaderRedactor redactor = RedactorOf(builder => builder
            .RedactHeaders("x-code-secret")
            .OverrideConfig(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Web:SensitiveHeaders:0"] = "x-configured-secret" })
            .LoadWebConfig());

        Assert.True(redactor.IsSensitive("x-configured-secret"));
        Assert.True(redactor.IsSensitive("x-code-secret"));
    }

    [Fact]
    public void TwoCallsInCodeBothCount()
    {
        HttpHeaderRedactor redactor = RedactorOf(builder => builder.LoadWebConfig().RedactHeaders("x-first").RedactHeaders("x-second"));

        Assert.True(redactor.IsSensitive("x-first"));
        Assert.True(redactor.IsSensitive("x-second"));
    }

    private static HttpHeaderRedactor RedactorOf(Func<IConfigInstanceBuilder, IConfigInstanceBuilder> configure)
    {
        ConfigInstance config = configure(ConfigInstance.Create()).Build();
        using Microsoft.Extensions.DependencyInjection.ServiceProvider services = config.BuildServiceProvider();

        return HttpHeaderRedactor.Resolve(services);
    }
}
