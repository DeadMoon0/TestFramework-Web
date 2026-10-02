using System;
using System.Collections.Generic;
using TestFramework.Web.Auth;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Core.Environment;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Builder.TimelineRunBuilder;
using TestFramework.Web.Configuration;
using TestFramework.Web.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace TestFramework.Web.Tests;

/// <summary>
/// Where an API is, when a container knows and the file does not.
/// </summary>
/// <remarks>
/// <para>
/// The second of Web's resources to be asked of the run rather than of a configuration store, and the one
/// with the most consumers behind it: every HTTP trigger and the liveness check read this record.
/// </para>
/// <para>
/// The cases mirror the stub's deliberately. They are the four questions worth asking of any resolver in this
/// family - does a container beat a file, does a file still answer alone, do the shape and the resolver agree
/// on every value name, and does a resource nobody declared work at all - and answering them the same way per
/// resource is how the remaining migrations stay comparable.
/// </para>
/// </remarks>
public class ApiResourceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task WhatAContainerPublishesWinsOverWhatTheFileSaid()
    {
        TimelineRun run = await Run(
            new PublishingEnvironment("orders", "http://127.0.0.1:49813/"),
            configured: [("Api:orders:BaseUrl", "http://localhost:5080/"), ("Api:orders:HealthPath", "/healthz")]);

        run.EnsureRanToCompletion();

        Assert.Equal("http://127.0.0.1:49813/", run.VariableStore.GetVariable<string>("address"));

        // Not published, so it still comes from the declaration - a produced value replaces one value, not
        // the entry.
        Assert.Equal("/healthz", run.VariableStore.GetVariable<string>("health"));
    }

    [Fact]
    public async Task WithNothingPublishedTheDeclarationStillAnswers()
    {
        TimelineRun run = await Run(
            environment: null,
            configured: [("Api:orders:BaseUrl", "http://localhost:5080/"), ("Api:orders:HealthPath", "/healthz")]);

        run.EnsureRanToCompletion();

        Assert.Equal("http://localhost:5080/", run.VariableStore.GetVariable<string>("address"));
        Assert.Equal("/healthz", run.VariableStore.GetVariable<string>("health"));
    }

    [Fact]
    public async Task TheShapeAndTheResolverAgreeOnEveryValue()
    {
        // The two directions of one mapping, written in different files: ApiShape puts a record into the
        // graph, ApiConfigResolver reads one back out. If the shape published a value under a name the
        // resolver does not read - or the other way round - this is what says so.
        TimelineRun run = await Run(
            new PublishingEnvironment("orders", "http://127.0.0.1:49813/", healthPath: "/ready"),
            configured: [("Api:orders:BaseUrl", "http://localhost:5080/"), ("Api:orders:HealthPath", "/healthz")]);

        run.EnsureRanToCompletion();

        Assert.Equal("http://127.0.0.1:49813/", run.VariableStore.GetVariable<string>("address"));
        Assert.Equal("/ready", run.VariableStore.GetVariable<string>("health"));
    }

    [Fact]
    public async Task AnApiTheRunStartedNeedsNoConfigurationEntryAtAll()
    {
        // The common case for a containerised application: it is declared as a container definition and
        // nowhere else, because its address does not exist until it is up. The health path falls back to the
        // record's own default, which is what the container default was a copy of.
        TimelineRun run = await Run(new PublishingEnvironment("orders", "http://127.0.0.1:49813/"));

        run.EnsureRanToCompletion();

        Assert.Equal("http://127.0.0.1:49813/", run.VariableStore.GetVariable<string>("address"));
        Assert.Equal("/health", run.VariableStore.GetVariable<string>("health"));
    }

    [Fact]
    public async Task AnApiNobodyDeclaredAndNothingStartedIsStillARefusal()
    {
        // Dropping the "an entry must exist" rule must not turn a misspelt identifier into an API with no
        // address.
        TimelineRun run = await Run(environment: null, resolves: "invoices", configured: [("Api:orders:BaseUrl", "http://localhost:5080/")]);

        TimelineRunFailedException failure = Assert.Throws<TimelineRunFailedException>(run.EnsureRanToCompletion);

        // Names the one that does exist, so a typo is a sentence rather than a hunt.
        Assert.Contains("orders", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsAndCredentialsTravelWithTheAddress()
    {
        // The whole entry now. An auth mode is an enum, a timeout is a TimeSpan, a key is a secret - all of
        // them declared in a file, carried as resource values, and handed back as the typed record a client
        // is built from. Nothing asked a service provider for any of it.
        TimelineRun run = await Run(
            environment: null,
            configured:
            [
                ("Api:orders:BaseUrl", "http://localhost:5080/"),
                ("Api:orders:Auth", "ApiKey"),
                ("Api:orders:ApiKeyHeaderName", "X-Api-Key"),
                ("Api:orders:ApiKey", "hunter2"),
                ("Api:orders:RequestTimeout", "00:00:30"),
            ]);

        run.EnsureRanToCompletion();

        Assert.Equal(ApiAuthMode.ApiKey, run.VariableStore.GetVariable<ApiAuthMode>("auth"));
        Assert.Equal(TimeSpan.FromSeconds(30), run.VariableStore.GetVariable<TimeSpan?>("timeout"));

        // The point read hands over the real key, because that is what asking for it means.
        Assert.Equal("hunter2", run.VariableStore.GetVariable<string>("api-key"));
    }

    [Fact]
    public async Task ACredentialIsRedactedInTheListingAndStillReachesTheClient()
    {
        // Both halves of the secret rule, in the one place that can show them together. The listing is what
        // failure messages, log lines and the frozen run are built from, so a key must not be in it - and the
        // record a client is built from must have it, or nothing could authenticate.
        TimelineRun run = await Run(
            environment: null,
            configured:
            [
                ("Api:orders:BaseUrl", "http://localhost:5080/"),
                ("Api:orders:Auth", "ApiKey"),
                ("Api:orders:ApiKeyHeaderName", "X-Api-Key"),
                ("Api:orders:ApiKey", "hunter2"),
            ]);

        run.EnsureRanToCompletion();

        Assert.Equal("hunter2", run.VariableStore.GetVariable<string>("api-key"));

        string listing = run.VariableStore.GetVariable<string>("listing")!;

        Assert.DoesNotContain("hunter2", listing, StringComparison.Ordinal);
        Assert.Contains("ApiKey=(secret)", listing, StringComparison.Ordinal);

        // And the same listing off the frozen run, which is where §5's "readable from the finished run"
        // lands - a reader can see that a key was configured without being shown it.
        Assert.DoesNotContain("hunter2", string.Join(" ", run.Values.Snapshot().Select(static value => value.Value)), StringComparison.Ordinal);

        // Not a blanket: the header name says which key was expected and is no use to anybody without it.
        Assert.Contains("X-Api-Key", listing, StringComparison.Ordinal);
    }

    private async Task<TimelineRun> Run(PublishingEnvironment? environment, string resolves = "orders", params (string Key, string Value)[] configured)
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new ResolvesApiStep(resolves)).Name("resolves")
            .Build();

        ITimelineRunBuilder builder = timeline.SetupRun(Configured(configured), output.WriteLine);

        if (environment is not null)
        {
            builder = builder.SetEnv(environment);
        }

        return await builder.RunAsync();
    }

    private static IServiceProvider Configured(params (string Key, string Value)[] values)
    {
        List<KeyValuePair<string, string?>> entries = [];

        foreach ((string key, string value) in values)
        {
            entries.Add(new KeyValuePair<string, string?>(key, value));
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(entries).Build();

        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.LoadWebConfigs(configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>Stands in for an application container: something that starts and then knows where it is.</summary>
    private sealed class PublishingEnvironment(string identifier, string baseUrl, string? healthPath = null) : EnvironmentProviderBase
    {
        private readonly EnvComponentIdentifier component = "api";

        public override IReadOnlyCollection<EnvComponentIdentifier> ResolveComponents(
            IEnumerable<Core.Artifacts.ArtifactInstanceGeneric> artifacts,
            IEnumerable<EnvironmentRequirement> requirements)
        {
            this.AddComponent(new PublishingComponent(this.component, identifier, baseUrl, healthPath));

            return [this.component];
        }
    }

    private sealed class PublishingComponent(EnvComponentIdentifier id, string identifier, string baseUrl, string? healthPath) : EnvComponent
    {
        public override EnvComponentIdentifier Id => id;

        public override Task<object?> CreateAsync(IEnvironmentProvider environment, RunContext context)
        {
            EnvironmentResources resources = PublishOn(context);

            resources.Produce(WebEnvironmentResourceKinds.RestApiKind, identifier, ValueNames.BaseUrl, ResourceVantage.Host, baseUrl);
            resources.Produce(WebEnvironmentResourceKinds.RestApiKind, identifier, ValueNames.BaseUrl, ResourceVantage.Network, "http://api:8080/");

            if (healthPath is not null)
            {
                resources.Produce(WebEnvironmentResourceKinds.RestApiKind, identifier, nameof(ApiConfig.HealthPath), healthPath);
            }

            return Task.FromResult<object?>(null);
        }

        public override Task DeconstructAsync(object? state, IEnvironmentProvider environment, RunContext context)
            => Task.CompletedTask;
    }

    /// <summary>Resolves the API the way every HTTP trigger in the package now does.</summary>
    private sealed class ResolvesApiStep(string identifier) : Step<EmptyStepResultContext>
    {
        public override string Name => "Resolves an API";

        public override string Description => "Resolves an API's configuration through the run.";

        public override bool DoesReturn => false;

        public override Step<EmptyStepResultContext> Clone() => new ResolvesApiStep(identifier).WithClonedOptions(this);

        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);

        public override void DeclareIO(StepIOContract contract)
        {
        }

        public override Task<EmptyStepResultContext?> Execute(RunContext context)
        {
            ApiConfig resolved = context.Configured<ApiConfig>(identifier);

            context.Variables.SetVariable("address", resolved.BaseUrl);
            context.Variables.SetVariable("health", resolved.HealthPath);
            context.Variables.SetVariable("auth", resolved.Auth);
            context.Variables.SetVariable("timeout", resolved.RequestTimeout);
            context.Variables.SetVariable("api-key", resolved.ApiKey);
            context.Variables.SetVariable("listing", string.Join(" | ", context.Values.Snapshot().Select(static value => $"{value.Key}={value.Value}")));

            return Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);
        }
    }
}
