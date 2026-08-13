using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using TestFramework.Web.Stub;
using TestFramework.Web.Stub.Mappings;
using Xunit;

namespace TestFramework.Web.Tests.Stub;

/// <summary>
/// Covers the declaration a stub server is given. It has to be data, because the server that runs it
/// may be in another process entirely.
/// </summary>
public class StubMappingTests
{
    private sealed class PaymentsStubDefinition : StubDefinition
    {
        public override StubIdentifier Identifier => "payments";

        protected override void Configure(StubMappingBuilder builder) => builder
            .OnGet("/api/rates/EUR")
                .RespondJson(HttpStatusCode.OK, new { currency = "EUR", rate = 1.08 })
            .OnPost("/api/charges")
                .WithHeader("Idempotency-Key")
                .WithBodyContaining("\"amount\"")
                .RespondJson(HttpStatusCode.Created, new { id = "{{Random Type=Guid}}" }, useTemplating: true)
            .OnGet("/api/slow")
                .RespondText(HttpStatusCode.OK, "ok", delay: TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Build_KeepsDeclarationOrderAndGivesEarlierMappingsPrecedence()
    {
        IReadOnlyList<StubMapping> mappings = new PaymentsStubDefinition().Build();

        Assert.Equal(["GET", "POST", "GET"], mappings.Select(mapping => mapping.Method));
        Assert.Equal(["/api/rates/EUR", "/api/charges", "/api/slow"], mappings.Select(mapping => mapping.Path));
        Assert.Equal([1, 2, 3], mappings.Select(mapping => mapping.Priority));
    }

    [Fact]
    public void On_NormalisesAPathWithoutALeadingSlash()
    {
        StubMappingBuilder builder = new();
        builder.OnGet("api/health").RespondStatus(HttpStatusCode.OK);

        Assert.Equal("/api/health", builder.Build()[0].Path);
    }

    [Fact]
    public void Write_ProducesTheRequestAndResponseShapeAStubServerLoads()
    {
        StubMapping mapping = new PaymentsStubDefinition().Build()[1];

        JsonNode node = JsonNode.Parse(StubMappingJson.Write(mapping))!;

        Assert.Equal(2, node["Priority"]!.GetValue<int>());
        Assert.Equal("/api/charges", node["Request"]!["Path"]!.GetValue<string>());
        Assert.Equal(["POST"], node["Request"]!["Methods"]!.AsArray().Select(method => method!.GetValue<string>()));
        Assert.Equal("Idempotency-Key", node["Request"]!["Headers"]![0]!["Name"]!.GetValue<string>());
        Assert.Equal("*\"amount\"*", node["Request"]!["Body"]!["Matchers"]![0]!["Pattern"]!.GetValue<string>());
        Assert.Equal(201, node["Response"]!["StatusCode"]!.GetValue<int>());
        Assert.Equal("application/json", node["Response"]!["Headers"]!["Content-Type"]!.GetValue<string>());
        Assert.True(node["Response"]!["UseTransformer"]!.GetValue<bool>());
    }

    [Fact]
    public void Write_CarriesADelayInMilliseconds()
    {
        StubMapping mapping = new PaymentsStubDefinition().Build()[2];

        JsonNode node = JsonNode.Parse(StubMappingJson.Write(mapping))!;

        Assert.Equal(2000, node["Response"]!["Delay"]!.GetValue<int>());
        Assert.Equal("ok", node["Response"]!["Body"]!.GetValue<string>());
    }

    [Fact]
    public void WriteAll_ProducesOneArrayInDeclarationOrder()
    {
        JsonNode node = JsonNode.Parse(StubMappingJson.WriteAll(new PaymentsStubDefinition().Build()))!;

        Assert.Equal(3, node.AsArray().Count);
        Assert.Equal("/api/rates/EUR", node[0]!["Request"]!["Path"]!.GetValue<string>());
    }

    [Fact]
    public void FileName_SortsInDeclarationOrder()
    {
        Assert.Equal("payments-001.json", StubMappingJson.FileName("payments", 1));
        Assert.Equal(
            ["payments-002.json", "payments-010.json"],
            new[] { StubMappingJson.FileName("payments", 10), StubMappingJson.FileName("payments", 2) }.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void WithPriority_OverridesDeclarationOrder()
    {
        StubMappingBuilder builder = new();
        builder.OnGet("/a").WithPriority(50).RespondStatus(HttpStatusCode.OK);
        builder.OnGet("/b").RespondStatus(HttpStatusCode.OK);

        Assert.Equal([50, 2], builder.Build().Select(mapping => mapping.Priority));
    }
}
