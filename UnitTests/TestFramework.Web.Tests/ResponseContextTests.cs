using System;
using System.Collections.Generic;
using System.Net;
using TestFramework.Web.Exceptions;
using TestFramework.Web.Http;
using Xunit;

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
    public void Summary_IdentifiesTheCallIncludingCorrelation()
    {
        HttpResponseContext response = Response(
            HttpStatusCode.InternalServerError,
            headers: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-correlation-id"] = ["corr-42"],
            });

        string summary = response.Summary();

        // Assertion failures render values through their string form, so this is what makes an
        // in-house assertion failure point at the exact call.
        Assert.Contains("GET http://localhost:5080/api/items", summary, StringComparison.Ordinal);
        Assert.Contains("500", summary, StringComparison.Ordinal);
        Assert.Contains("x-correlation-id=corr-42", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_AddsABodyExcerpt_WhenTheStatusIsNotSuccessful()
    {
        string rendered = Response(HttpStatusCode.InternalServerError, """{"error":"boom"}""").ToString();

        Assert.Contains("500", rendered, StringComparison.Ordinal);
        Assert.Contains("boom", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_StaysCompact_WhenTheStatusIsSuccessful()
    {
        HttpResponseContext response = Response(HttpStatusCode.OK, """{"name":"widget"}""");

        Assert.Equal(response.Summary(), response.ToString());
        Assert.DoesNotContain("widget", response.ToString(), StringComparison.Ordinal);
    }
}
