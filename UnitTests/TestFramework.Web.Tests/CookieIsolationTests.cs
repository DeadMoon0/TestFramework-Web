using System.Threading.Tasks;
using TestFramework.Config;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Web.SampleApi;
using Xunit;

namespace TestFramework.Web.Tests;

/// <summary>
/// Guards the cookie behaviour of the pooled client: state a run picks up must not reach the next.
/// </summary>
/// <remarks>
/// Clients are pooled in a process-wide dictionary keyed by identifier and connection settings, so
/// two runs against the same identifier deliberately share one handler. That is what makes a cookie
/// jar dangerous, and what these tests pin down.
/// </remarks>
[Collection(SampleApiCollection.CollectionName)]
public class CookieIsolationTests(SampleApiFixture fixture)
{
    [Fact]
    public async Task Cookies_AreNotCarriedIntoTheNextRun()
    {
        Timeline issuing = Timeline.Create()
            .Trigger(WebExt.Api.Http("cookiesoff").Get("api/cookie/set").Call()).Name("set")
            .Build();

        TimelineRun first = await issuing.SetupRun(fixture.CreateConfig("cookiesoff")).RunAsync();
        first.EnsureRanToCompletion();

        Timeline echoing = Timeline.Create()
            .Trigger(WebExt.Api.Http("cookiesoff").Get("api/cookie/echo").Call()).Name("echo")
            .Build();

        TimelineRun second = await echoing.SetupRun(fixture.CreateConfig("cookiesoff")).RunAsync();

        second.EnsureRanToCompletion();
        using (second.AssertionScope())
        {
            second.ApiJson<CookieEchoResponse>("echo").Should()
                .Match(echo => !echo.HadCookieHeader, "the second run sent no Cookie header")
                .And().Match(echo => echo.SessionCookie is null, "the first run's session did not leak");
        }
    }

    [Fact]
    public async Task Cookies_AreKept_WhenTheIdentifierAsksForThem()
    {
        // The opposite direction, so the switch is proven to do something: with UseCookies on, the
        // jar is shared - which is precisely why it is off by default.
        ConfigInstance Config() => fixture.CreateConfig("cookieson", values => values["Api:cookieson:UseCookies"] = "true");

        Timeline issuing = Timeline.Create()
            .Trigger(WebExt.Api.Http("cookieson").Get("api/cookie/set").Call()).Name("set")
            .Build();

        TimelineRun first = await issuing.SetupRun(Config()).RunAsync();
        first.EnsureRanToCompletion();

        Timeline echoing = Timeline.Create()
            .Trigger(WebExt.Api.Http("cookieson").Get("api/cookie/echo").Call()).Name("echo")
            .Build();

        TimelineRun second = await echoing.SetupRun(Config()).RunAsync();

        second.EnsureRanToCompletion();
        second.ApiJson<CookieEchoResponse>("echo").Should()
            .Match(echo => echo.SessionCookie == "sample-session", "the shared jar replayed the session");
    }
}
