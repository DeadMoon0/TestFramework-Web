using System.Net;
using TestFramework.Config;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web.Exceptions;
using TestFramework.Web.SampleApi;
using TestFramework.Web.Trigger.IsLive;

namespace TestFramework.Web.Tests;

/// <summary>
/// Drives the real sender against the sample API over a real socket, asserting through the
/// framework's own fluent assertions so every check is signalled to the debugging UI.
/// </summary>
[Collection(SampleApiCollection.CollectionName)]
public class ApiTriggerTests(SampleApiFixture fixture)
{
    [Fact]
    public async Task Get_ReturnsTheResponseBodyAndStatus()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("sample").Get("api/items").Call()).Name("list")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.Step("list").Should().HaveCompleted();
        run.ApiStatus("list").Should().Be(HttpStatusCode.OK);

        // The sample API is shared across this collection and other tests create items, so assert on
        // the seeded data rather than on an exact count.
        run.ApiJson<SampleItem[]>("list").Should()
            .Match(items => items.Any(item => item.Name == "first"), "contains the seeded item")
            .And().Match(items => items.Length >= 3, "has at least the seeded items");
    }

    [Fact]
    public async Task Get_AppliesQueryValuesFromVariables()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("take", Var.Const("2"))
            .Trigger(WebExt.Api.Http("sample")
                .Get("api/items")
                .WithQuery("take", Var.Ref<string>("take"))
                .Call()).Name("list")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.ApiJson<SampleItem[]>("list").Should().HaveCount(2);
    }

    [Fact]
    public async Task Get_SubstitutesRouteValuesFromVariables()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("itemId", Var.Const("2"))
            .Trigger(WebExt.Api.Http("sample")
                .Get("api/items/{id}")
                .WithRouteValue("id", Var.Ref<string>("itemId"))
                .Call()).Name("get-item")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.ApiJson<SampleItem>("get-item").Select(item => item.Id).Should().Be("2");
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WithoutFailingTheStep()
    {
        // An unsuccessful status is a result, not a transport failure: the timeline must survive it
        // so tests can assert the API's error behaviour.
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("sample").Get("api/items/does-not-exist").Call()).Name("missing")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.Step("missing").Should().HaveCompleted();
        run.ApiStatus("missing").Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_SendsAJsonBodyAndCapturesTheCreatedLocation()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("payload", Var.Const(new CreateSampleItem("created-by-test", 7)))
            .Trigger(WebExt.Api.Http("sample")
                .Post("api/items")
                .WithJsonBody(Var.Ref<CreateSampleItem>("payload"))
                .Call()).Name("create")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();

        // One scope, so a failing header check does not hide a failing body check.
        using (run.AssertionScope())
        {
            run.ApiStatus("create").Should().Be(HttpStatusCode.Created);
            run.ApiHeader("create", "Location").Should().StartWith("/api/items/");
            run.ApiJson<SampleItem>("create").Select(item => item.Name).Should().Be("created-by-test");
        }
    }

    [Fact]
    public async Task Post_DeclaresTheJsonContentType()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("payload", Var.Const(new CreateSampleItem("echo", 1)))
            .Trigger(WebExt.Api.Http("sample")
                .Post("api/echo")
                .WithJsonBody(Var.Ref<CreateSampleItem>("payload"))
                .Call()).Name("echo")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.ApiBody("echo").Should().Contain("application/json").And().Contain("echo");
    }

    [Fact]
    public async Task Headers_FromVariablesReachTheServer()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("correlationId", Var.Const("corr-4711"))
            .Trigger(WebExt.Api.Http("sample")
                .Get("api/echo")
                .WithHeader(Var.Const("x-correlation-id"), Var.Ref<string>("correlationId"))
                .Call()).Name("echo")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.ApiBody("echo").Should().Contain("corr-4711");
    }

    [Fact]
    public async Task ConfiguredApiKey_AuthenticatesTheSecuredEndpoint()
    {
        ConfigInstance config = fixture.CreateConfig("secured", values =>
        {
            values["Api:secured:Auth"] = "ApiKey";
            values["Api:secured:ApiKeyHeaderName"] = SampleApiHost.ApiKeyHeaderName;
            values["Api:secured:ApiKey"] = SampleApiHost.ApiKeyValue;
        });

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("secured").Get("api/secure").Call()).Name("secure")
            .Build();

        TimelineRun run = await timeline.SetupRun(config).RunAsync();

        run.EnsureRanToCompletion();
        run.ApiStatus("secure").Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MissingCredentials_ProduceAnUnauthorizedResponse()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("sample").Get("api/secure").Call()).Name("secure")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.ApiStatus("secure").Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PerRequestBearerToken_OverridesTheConfiguredMode()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("token", Var.Const(SampleApiHost.BearerTokenValue))
            .Trigger(WebExt.Api.Http("sample")
                .Get("api/secure")
                .WithBearerToken(Var.Ref<string>("token"))
                .Call()).Name("secure")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.ApiStatus("secure").Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProblemDetails_AreReturnedAsABodyRatherThanAFailure()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("sample").Get("api/problem").Call()).Name("problem")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        using (run.AssertionScope())
        {
            run.ApiStatus("problem").Should().Be(HttpStatusCode.BadRequest);
            run.ApiResponse("problem").Select(response => response.ContentType).Should().Contain("problem+json");
            run.ApiBody("problem").Should().Contain("Sample problem");
        }
    }

    [Fact]
    public async Task WarmupStatus_IsRetriedAgainstALocalHost()
    {
        // The sample API answers 404 twice for this key before succeeding, which is exactly the
        // startup behaviour the warmup rule exists to absorb.
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("sample")
                .Get("api/flaky")
                .WithQuery("key", Var.Const($"warmup-{Guid.NewGuid()}"))
                .WithQuery("failures", Var.Const("2"))
                .Call()).Name("flaky")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.ApiStatus("flaky").Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequestTimeout_SurfacesAsAnActionableTransportFailure()
    {
        ConfigInstance config = fixture.CreateConfig("slow", values => values["Api:slow:RequestTimeout"] = "00:00:00.250");

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("slow")
                .Get("api/slow")
                .WithQuery("delayMs", Var.Const("3000"))
                .Call()).Name("slow")
            .Build();

        TimelineRun run = await timeline.SetupRun(config).RunAsync();

        run.Step("slow").Should().HaveErrored().And().HaveThrown<ApiRequestFailedException>();
        run.Assert(run.Step("slow").LastResult.Exception!.Message, "transport failure message")
            .Should().Contain("RequestTimeout");
    }

    [Fact]
    public async Task UnreachableHost_SurfacesAsAnActionableTransportFailure()
    {
        ConfigInstance config = fixture.CreateConfig("dead", values => values["Api:dead:BaseUrl"] = "http://127.0.0.1:1/");

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("dead").Get("api/items").Call()).Name("dead")
            .Build();

        TimelineRun run = await timeline.SetupRun(config).RunAsync();

        run.Step("dead").Should().HaveThrown<ApiRequestFailedException>();
        run.Assert(run.Step("dead").LastResult.Exception!.Message, "transport failure message")
            .Should().Contain("127.0.0.1:1").And().Contain("BaseUrl");
    }

    [Fact]
    public async Task UnknownIdentifier_FailsWithTheRegisteredIdentifiersListed()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("not-registered").Get("api/items").Call()).Name("unknown")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.Step("unknown").Should().HaveThrown<ApiConfigurationValidationException>();
        run.Assert(run.Step("unknown").LastResult.Exception!.Message, "configuration failure message")
            .Should().Contain("not-registered").And().Contain("sample");
    }

    [Fact]
    public async Task IsLive_Reachable_SucceedsAgainstAnyStatusCode()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.IsLive("sample", ApiAlivenessLevel.Reachable)).Name("reachable")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        using (run.AssertionScope())
        {
            run.ApiProbe("reachable").Select(probe => probe.Success).Should().Be(true);
            run.ApiProbe("reachable").Select(probe => probe.AlivenessLevel).Should().Be(ApiAlivenessLevel.Reachable);
        }
    }

    [Fact]
    public async Task IsLive_Healthy_ProbesTheConfiguredHealthPath()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.IsLive("sample", ApiAlivenessLevel.Healthy)).Name("healthy")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        using (run.AssertionScope())
        {
            run.ApiProbe("healthy").Select(probe => probe.StatusCode).Should().Be(HttpStatusCode.OK);
            run.ApiProbe("healthy").Select(probe => probe.ProbeUri.AbsolutePath).Should().EndWith("health");
        }
    }

    [Fact]
    public async Task IsLive_Healthy_Fails_WhenTheHealthPathDoesNotExist()
    {
        ConfigInstance config = fixture.CreateConfig("nohealth", values => values["Api:nohealth:HealthPath"] = "/no-such-health");

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.IsLive("nohealth", ApiAlivenessLevel.Healthy)).Name("healthy")
            .Build();

        TimelineRun run = await timeline.SetupRun(config).RunAsync();

        run.Step("healthy").Should().HaveThrown<ApiLivenessProbeException>();
        run.Assert(run.Step("healthy").LastResult.Exception!.Message, "probe failure message")
            .Should().Contain("HealthPath");
    }
}
