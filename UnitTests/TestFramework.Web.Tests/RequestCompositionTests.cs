using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using TestFramework.Core.Variables;
using TestFramework.Web.Auth;
using TestFramework.Web.Exceptions;
using TestFramework.Web.Http;
using Xunit;

namespace TestFramework.Web.Tests;

/// <summary>
/// Covers URI composition and message construction, which are the two places a request can go
/// silently wrong. Variable resolution is covered end to end in <see cref="ApiTriggerTests"/>.
/// </summary>
public class RequestCompositionTests
{
    private static HttpRequestSpec Spec(
        string path,
        HttpMethod? method = null,
        Dictionary<string, string>? routeValues = null,
        List<KeyValuePair<string, string>>? query = null,
        List<KeyValuePair<string, string[]>>? headers = null,
        string? bodyText = null,
        byte[]? bodyBytes = null,
        string? contentType = null,
        IApiAuthenticationProvider? authOverride = null)
        => new(
            method ?? HttpMethod.Get,
            path,
            routeValues ?? new Dictionary<string, string>(StringComparer.Ordinal),
            query ?? [],
            headers ?? [],
            bodyText,
            bodyBytes,
            contentType,
            authOverride);

    [Theory]
    [InlineData("http://localhost:5080/", "api/items", "http://localhost:5080/api/items")]
    [InlineData("http://localhost:5080", "api/items", "http://localhost:5080/api/items")]
    [InlineData("http://localhost:5080/", "/api/items", "http://localhost:5080/api/items")]
    [InlineData("http://localhost:5080/root/", "api/items", "http://localhost:5080/root/api/items")]
    public void ResolveUri_ComposesBaseUrlAndPath(string baseUrl, string path, string expected)
        => Assert.Equal(expected, Spec(path).ResolveUri("sample", baseUrl).AbsoluteUri);

    [Fact]
    public void ResolveUri_KeepsTheBasePath_WhenThePathHasALeadingSlash()
    {
        // A leading slash must not discard a configured base path; that would silently retarget the call.
        Uri uri = Spec("/api/items").ResolveUri("sample", "http://localhost:5080/root/");

        Assert.Equal("http://localhost:5080/root/api/items", uri.AbsoluteUri);
    }

    [Fact]
    public void ResolveUri_SubstitutesAndEscapesRouteValues()
    {
        HttpRequestSpec spec = Spec("api/items/{id}", routeValues: new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = "a b/c" });

        Assert.Equal("http://localhost:5080/api/items/a%20b%2Fc", spec.ResolveUri("sample", "http://localhost:5080/").AbsoluteUri);
    }

    [Fact]
    public void ResolveUri_Throws_WhenARouteTokenWasNotSupplied()
    {
        ApiConfigurationValidationException exception = Assert.Throws<ApiConfigurationValidationException>(
            () => Spec("api/items/{id}").ResolveUri("sample", "http://localhost:5080/"));

        Assert.Contains("WithRouteValue", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveUri_EscapesQueryValues()
    {
        HttpRequestSpec spec = Spec("api/items", query:
        [
            new KeyValuePair<string, string>("name", "a&b c"),
            new KeyValuePair<string, string>("take", "2"),
        ]);

        Assert.Equal(
            "http://localhost:5080/api/items?name=a%26b%20c&take=2",
            spec.ResolveUri("sample", "http://localhost:5080/").AbsoluteUri);
    }

    [Fact]
    public void ResolveUri_Throws_WhenBaseUrlIsNotAbsolute()
        => Assert.Throws<ApiConfigurationValidationException>(() => Spec("api/items").ResolveUri("sample", "not-a-url"));

    [Fact]
    public void CreateMessage_AppliesSingleAndMultiValuedHeaders()
    {
        HttpRequestSpec spec = Spec("api/items", headers:
        [
            new KeyValuePair<string, string[]>("x-single", ["one"]),
            new KeyValuePair<string, string[]>("x-multi", ["a", "b"]),
        ]);

        using HttpRequestMessage message = spec.CreateMessage(new Uri("http://localhost:5080/api/items"));

        Assert.Equal(["one"], message.Headers.GetValues("x-single"));
        Assert.Equal(["a", "b"], message.Headers.GetValues("x-multi"));
    }

    [Fact]
    public void CreateMessage_SetsTextBodyAndContentType()
    {
        HttpRequestSpec spec = Spec("api/items", HttpMethod.Post, bodyText: "payload", contentType: "text/plain; charset=utf-8");

        using HttpRequestMessage message = spec.CreateMessage(new Uri("http://localhost:5080/api/items"));

        Assert.NotNull(message.Content);
        Assert.Equal("text/plain; charset=utf-8", message.Content!.Headers.ContentType?.ToString());
    }

    [Fact]
    public void CreateMessage_SetsBinaryBody()
    {
        HttpRequestSpec spec = Spec("api/items", HttpMethod.Post, bodyBytes: [1, 2, 3], contentType: "application/octet-stream");

        using HttpRequestMessage message = spec.CreateMessage(new Uri("http://localhost:5080/api/items"));

        Assert.NotNull(message.Content);
        Assert.Equal("application/octet-stream", message.Content!.Headers.ContentType?.ToString());
    }

    [Fact]
    public void BuildVariable_TracksIdentifierBackedInputsOnly()
    {
        ApiRequestBuilderState state = new();
        state.SetEndpoint(HttpMethod.Get, Var.Const("api/items/{id}"));
        state.AddRouteValue("id", Var.Ref<string>("itemId"));
        state.AddQuery("take", Var.Const("2"));

        string[] identifiers = [.. state.BuildVariable().Inputs.Select(input => input.Identifier!.Identifier)];

        // Constants carry no identifier, so only the referenced variable becomes a declared input.
        Assert.Equal(["itemId"], identifiers);
    }
}
