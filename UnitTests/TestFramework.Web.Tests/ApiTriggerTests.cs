using System.Net;
using TestFramework.Config;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.Web.Exceptions;
using TestFramework.Web.Http;
using TestFramework.Web.SampleApi;
using TestFramework.Web.Trigger.IsLive;

namespace TestFramework.Web.Tests;

/// <summary>
/// Drives the real sender against the sample API over a real socket.
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
        SampleItem[] items = run.Step("list").ExpectJson<SampleItem[]>();

        // The sample API is shared across this collection and other tests create items, so assert
        // on the seeded data rather than on an exact count.
        Assert.Contains(items, item => item.Name == "first");
        Assert.True(items.Length >= 3);
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
        Assert.Equal(2, run.Step("list").ExpectJson<SampleItem[]>().Length);
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
        Assert.Equal("2", run.Step("get-item").ExpectJson<SampleItem>().Id);
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
        Assert.Equal(HttpStatusCode.NotFound, run.Step("missing").Response().StatusCode);
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
        HttpResponseContext response = run.Step("create").ExpectStatus(HttpStatusCode.Created);
        Assert.StartsWith("/api/items/", response.Header("Location")!, StringComparison.Ordinal);
        Assert.Equal("created-by-test", response.Json<SampleItem>().Name);
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
        string body = run.Step("echo").ExpectSuccess().Body!;
        Assert.Contains("application/json", body, StringComparison.Ordinal);
        Assert.Contains("echo", body, StringComparison.Ordinal);
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
        Assert.Contains("corr-4711", run.Step("echo").ExpectSuccess().Body!, StringComparison.Ordinal);
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
        run.Step("secure").ExpectStatus(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MissingCredentials_ProduceAnUnauthorizedResponse()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("sample").Get("api/secure").Call()).Name("secure")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(HttpStatusCode.Unauthorized, run.Step("secure").Response().StatusCode);
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
        run.Step("secure").ExpectStatus(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProblemDetails_AreReturnedAsABodyRatherThanAFailure()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("sample").Get("api/problem").Call()).Name("problem")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        HttpResponseContext response = run.Step("problem").Response();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("problem+json", response.ContentType!, StringComparison.Ordinal);
        Assert.Contains("Sample problem", response.Body!, StringComparison.Ordinal);
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
        run.Step("flaky").ExpectStatus(HttpStatusCode.OK);
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

        Assert.Throws<TimelineRunFailedException>(run.EnsureRanToCompletion);
        ApiRequestFailedException failure = FindApiRequestFailure(run.Step("slow").LastResult.Exception);
        Assert.Contains("RequestTimeout", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreachableHost_SurfacesAsAnActionableTransportFailure()
    {
        ConfigInstance config = fixture.CreateConfig("dead", values => values["Api:dead:BaseUrl"] = "http://127.0.0.1:1/");

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("dead").Get("api/items").Call()).Name("dead")
            .Build();

        TimelineRun run = await timeline.SetupRun(config).RunAsync();

        Assert.Throws<TimelineRunFailedException>(run.EnsureRanToCompletion);
        ApiRequestFailedException failure = FindApiRequestFailure(run.Step("dead").LastResult.Exception);
        Assert.Contains("127.0.0.1:1", failure.Message, StringComparison.Ordinal);
        Assert.Contains("BaseUrl", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownIdentifier_FailsWithTheRegisteredIdentifiersListed()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.Http("not-registered").Get("api/items").Call()).Name("unknown")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        Assert.Throws<TimelineRunFailedException>(run.EnsureRanToCompletion);
        Exception? exception = run.Step("unknown").LastResult.Exception;
        ApiConfigurationValidationException failure = FindException<ApiConfigurationValidationException>(exception);
        Assert.Contains("not-registered", failure.Message, StringComparison.Ordinal);
        Assert.Contains("sample", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IsLive_Reachable_SucceedsAgainstAnyStatusCode()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.IsLive("sample", ApiAlivenessLevel.Reachable)).Name("reachable")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        ApiIsLiveResult result = run.Step("reachable").ProbeResult();
        Assert.True(result.Success);
        Assert.Equal(ApiAlivenessLevel.Reachable, result.AlivenessLevel);
    }

    [Fact]
    public async Task IsLive_Healthy_ProbesTheConfiguredHealthPath()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.IsLive("sample", ApiAlivenessLevel.Healthy)).Name("healthy")
            .Build();

        TimelineRun run = await timeline.SetupRun(fixture.CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        ApiIsLiveResult result = run.Step("healthy").ProbeResult();
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.EndsWith("health", result.ProbeUri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IsLive_Healthy_Fails_WhenTheHealthPathDoesNotExist()
    {
        ConfigInstance config = fixture.CreateConfig("nohealth", values => values["Api:nohealth:HealthPath"] = "/no-such-health");

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Api.IsLive("nohealth", ApiAlivenessLevel.Healthy)).Name("healthy")
            .Build();

        TimelineRun run = await timeline.SetupRun(config).RunAsync();

        Assert.Throws<TimelineRunFailedException>(run.EnsureRanToCompletion);
        ApiLivenessProbeException failure = FindException<ApiLivenessProbeException>(run.Step("healthy").LastResult.Exception);
        Assert.Contains("HealthPath", failure.Message, StringComparison.Ordinal);
    }

    private static ApiRequestFailedException FindApiRequestFailure(Exception? exception)
        => FindException<ApiRequestFailedException>(exception);

    private static TException FindException<TException>(Exception? exception) where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException match)
                return match;
        }

        throw new InvalidOperationException($"No {typeof(TException).Name} was found in the exception chain: {exception?.ToString() ?? "(none)"}");
    }
}
