using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Web.Stub.Admin;
using TestFramework.Web.Stub.Exceptions;
using Xunit;

namespace TestFramework.Web.Tests.Stub;

/// <summary>
/// Covers reading a stub's request log, which is what every stub assertion rests on.
/// </summary>
public class StubAdminClientTests
{
    private const string TwoCalls = """
        [
          {
            "Guid": "11111111-1111-1111-1111-111111111111",
            "MappingGuid": "22222222-2222-2222-2222-222222222222",
            "Request": {
              "Method": "POST",
              "Path": "/api/charges",
              "DateTime": "2026-08-13T10:00:01Z",
              "Body": "{\"amount\":42}",
              "Headers": { "Content-Type": [ "application/json" ], "Idempotency-Key": "abc" }
            }
          },
          {
            "Guid": "33333333-3333-3333-3333-333333333333",
            "Request": {
              "Method": "GET",
              "Path": "/api/unknown",
              "DateTime": "2026-08-13T10:00:00Z"
            },
            "RequestMatchResult": { "IsPerfectMatch": false }
          }
        ]
        """;

    private static StubAdminClient CreateClient(string payload, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(
            new HttpClient(new StubHandler(payload, statusCode)) { BaseAddress = new Uri("http://localhost:9999/") },
            "/__admin");

    [Fact]
    public async Task GetCallsAsync_ReadsMethodPathBodyAndHeaders()
    {
        IReadOnlyList<StubCall> calls = await CreateClient(TwoCalls).GetCallsAsync(CancellationToken.None);

        StubCall charge = calls.Single(call => call.Path == "/api/charges");

        Assert.Equal("POST", charge.Method);
        Assert.Equal("{\"amount\":42}", charge.Body);
        Assert.Equal("application/json", charge.Headers["Content-Type"]);
        Assert.Equal("abc", charge.Headers["Idempotency-Key"]);
        Assert.True(charge.Matched);
    }

    [Fact]
    public async Task GetCallsAsync_OrdersCallsOldestFirst()
    {
        IReadOnlyList<StubCall> calls = await CreateClient(TwoCalls).GetCallsAsync(CancellationToken.None);

        Assert.Equal(["/api/unknown", "/api/charges"], calls.Select(call => call.Path));
    }

    [Fact]
    public async Task GetCallsAsync_MarksACallNoMappingAnsweredAsUnmatched()
    {
        IReadOnlyList<StubCall> calls = await CreateClient(TwoCalls).GetCallsAsync(CancellationToken.None);

        Assert.False(calls.Single(call => call.Path == "/api/unknown").Matched);
    }

    [Fact]
    public async Task GetCallsAsync_ReturnsNothingForAnEmptyLog()
        => Assert.Empty(await CreateClient("[]").GetCallsAsync(CancellationToken.None));

    [Fact]
    public async Task GetCallsAsync_FailsWithTheStatusWhenTheAdminSurfaceRefuses()
    {
        StubAdminException exception = await Assert.ThrowsAsync<StubAdminException>(
            () => CreateClient("nope", HttpStatusCode.NotFound).GetCallsAsync(CancellationToken.None));

        Assert.Contains("404", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCallsAsync_FailsClearlyWhenTheAddressIsNotAStubServer()
    {
        StubAdminException exception = await Assert.ThrowsAsync<StubAdminException>(
            () => CreateClient("<html>hello</html>").GetCallsAsync(CancellationToken.None));

        Assert.Contains("not JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMappingCountAsync_CountsWhatTheServerActuallyLoaded()
        => Assert.Equal(2, await CreateClient("[{},{}]").GetMappingCountAsync(CancellationToken.None));

    private sealed class StubHandler(string payload, HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(payload) });
    }
}
