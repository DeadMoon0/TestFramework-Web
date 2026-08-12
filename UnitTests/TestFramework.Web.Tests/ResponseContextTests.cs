using System.Collections.Generic;
using System.Net;
using TestFramework.Web.Exceptions;
using TestFramework.Web.Http;

namespace TestFramework.Web.Tests;

public class ResponseContextTests
{
    private static HttpResponseContext Response(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? body = null,
        string? contentType = "application/json",
        Dictionary<string, string[]>? headers = null)
        => new(
            "sample",
            "GET",
            new Uri("http://localhost:5080/api/items"),
            statusCode,
            statusCode.ToString(),
            contentType,
            body,
            headers ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            TimeSpan.FromMilliseconds(12));

    private sealed record Payload(string Name, int Quantity);

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.Found, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public void IsSuccess_ReflectsThe2xxRange(HttpStatusCode statusCode, bool expected)
        => Assert.Equal(expected, Response(statusCode).IsSuccess);

    [Fact]
    public void Json_DeserializesWithWebNamingRules()
    {
        HttpResponseContext response = Response(body: """{"name":"widget","quantity":3}""");

        Payload payload = response.Json<Payload>();

        Assert.Equal("widget", payload.Name);
        Assert.Equal(3, payload.Quantity);
    }

    [Fact]
    public void Json_Throws_WithStatusAndContentTypeNamed_WhenBodyIsNotJson()
    {
        HttpResponseContext response = Response(HttpStatusCode.BadRequest, "not json at all", "text/plain");

        ApiResponseFormatException exception = Assert.Throws<ApiResponseFormatException>(response.Json<Payload>);

        Assert.Contains("400", exception.Message, StringComparison.Ordinal);
        Assert.Contains("text/plain", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not json at all", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_Throws_WhenThereIsNoBody()
        => Assert.Throws<ApiResponseFormatException>(Response(HttpStatusCode.NoContent, null).Json<Payload>);

    [Fact]
    public void Header_IsCaseInsensitiveAndReturnsNullWhenAbsent()
    {
        HttpResponseContext response = Response(headers: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Location"] = ["/api/items/4"],
        });

        Assert.Equal("/api/items/4", response.Header("location"));
        Assert.Null(response.Header("x-missing"));
    }

    [Fact]
    public void BodyExcerpt_TruncatesLongBodiesAndReportsTheFullLength()
    {
        string body = new('x', 5000);

        string excerpt = Assert.IsType<string>(Response(body: body).BodyExcerpt());

        Assert.StartsWith("xxx", excerpt, StringComparison.Ordinal);
        Assert.Contains("truncated", excerpt, StringComparison.Ordinal);
        Assert.Contains("5000", excerpt, StringComparison.Ordinal);
        Assert.True(excerpt.Length < body.Length);
    }

    [Fact]
    public void BodyExcerpt_ReturnsNull_WhenThereIsNoBody()
        => Assert.Null(Response(body: null).BodyExcerpt());

    [Fact]
    public void StatusAssertionFailure_CarriesEnoughContextToDiagnoseWithoutRerunning()
    {
        HttpResponseContext response = Response(
            HttpStatusCode.InternalServerError,
            """{"error":"boom"}""",
            headers: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-correlation-id"] = ["corr-42"],
            });

        ApiStatusAssertionException exception = ApiStatusAssertionException.Mismatch(response, "200 OK");

        Assert.Contains("200 OK", exception.Message, StringComparison.Ordinal);
        Assert.Contains("500", exception.Message, StringComparison.Ordinal);
        Assert.Contains("GET http://localhost:5080/api/items", exception.Message, StringComparison.Ordinal);
        Assert.Contains("corr-42", exception.Message, StringComparison.Ordinal);
        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
        Assert.Contains("server-side fault", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusAssertionFailure_PointsAtAuthConfiguration_OnUnauthorized()
    {
        ApiStatusAssertionException exception = ApiStatusAssertionException.Mismatch(Response(HttpStatusCode.Unauthorized), "200 OK");

        Assert.Contains("Negotiate", exception.Message, StringComparison.Ordinal);
    }
}
