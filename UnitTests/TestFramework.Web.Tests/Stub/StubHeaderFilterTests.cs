using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestFramework.Config;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.Web.Extensions;
using Xunit;

namespace TestFramework.Web.Tests.Stub;

/// <summary>
/// A header filter read from a variable: each run filters on its own value, and the run knows it reads one.
/// </summary>
public class StubHeaderFilterTests
{
    private static ConfigInstance Config() => ConfigInstance.Create()
        .OverrideConfig(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Stub:payments:BaseUrl"] = "http://localhost:8080/",
            ["Stub:payments:AdminPath"] = "/__admin",
        })
        .LoadWebConfig()
        .Build();

    [Fact]
    public async Task ACallsFilterOnAVariableNothingWrites_IsRefusedBeforeTheRunStarts()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Stub.Calls("payments").WithHeader("x-correlation-id", Var.Ref<string>("correlationId"))).Label("calls")
            .Build();

        TimelineFrameworkException refused = await Assert.ThrowsAnyAsync<TimelineFrameworkException>(() => timeline.SetupRun(Config()).RunAsync());

        Assert.Contains("correlationId", refused.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWaitFilteringOnAVariableNothingWrites_IsRefusedBeforeTheRunStarts()
    {
        Timeline timeline = Timeline.Create()
            .WaitForEvent(WebExt.Stub.Called("payments", System.Net.Http.HttpMethod.Post, "/api/charges")
                .WithHeader("x-correlation-id", Var.Ref<string>("correlationId"))).Label("charged")
            .Build();

        TimelineFrameworkException refused = await Assert.ThrowsAnyAsync<TimelineFrameworkException>(() => timeline.SetupRun(Config()).RunAsync());

        Assert.Contains("correlationId", refused.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AFilterOnAVariable_IsDescribedByTheVariablesName()
    {
        string described = WebExt.Stub.Calls("payments")
            .WithHeader("x-correlation-id", Var.Ref<string>("correlationId"))
            .WithHeader("x-tenant", "acme")
            .Description;

        Assert.Contains("x-correlation-id={correlationId}", described, StringComparison.Ordinal);
        Assert.Contains("x-tenant=acme", described, StringComparison.Ordinal);
    }
}
