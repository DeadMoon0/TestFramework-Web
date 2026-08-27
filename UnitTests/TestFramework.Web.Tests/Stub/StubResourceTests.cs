using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Core.Environment;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Builder.TimelineRunBuilder;
using TestFramework.Web.Extensions;
using TestFramework.Web.Stub;
using Xunit;
using Xunit.Abstractions;

namespace TestFramework.Web.Tests.Stub;

/// <summary>
/// Where a stub is, when a container knows and the file does not.
/// </summary>
/// <remarks>
/// <para>
/// The first of Web's three resources to be asked of the run rather than of a configuration store. A
/// configured stub has an address somebody wrote down; a containerised one gets its address when its container
/// starts, on a port the operating system chose. The resolver has to answer both, and prefer the one that
/// started, because a file cannot hold an ephemeral port and so holds a default instead.
/// </para>
/// <para>
/// Also the reason the resolver takes the run instead of a service provider: a produced value exists on the
/// run and nowhere else, so a resolver handed only services cannot see one no matter how it is written.
/// </para>
/// </remarks>
public class StubResourceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task WhatAContainerPublishesWinsOverWhatTheFileSaid()
    {
        // The whole point. The file has a placeholder port; something started and learned the real one.
        TimelineRun run = await Run(
            new PublishingEnvironment("payments", "http://127.0.0.1:49812/"),
            configured: [("Stub:payments:BaseUrl", "http://localhost:8080/"), ("Stub:payments:AdminPath", "/__admin")]);

        run.EnsureRanToCompletion();

        Assert.Equal("http://127.0.0.1:49812/", run.VariableStore.GetVariable<string>("address"));

        // Not published, so it still comes from the declaration - a produced value replaces one value, not
        // the entry.
        Assert.Equal("/__admin", run.VariableStore.GetVariable<string>("admin"));
    }

    [Fact]
    public async Task WithNothingPublishedTheDeclarationStillAnswers()
    {
        // A stub somebody runs themselves: the file is the only source, and reading through the run has to
        // work exactly as well as reading the store did.
        TimelineRun run = await Run(
            environment: null,
            configured: [("Stub:payments:BaseUrl", "http://localhost:8080/"), ("Stub:payments:AdminPath", "/__admin")]);

        run.EnsureRanToCompletion();

        Assert.Equal("http://localhost:8080/", run.VariableStore.GetVariable<string>("address"));
        Assert.Equal("/__admin", run.VariableStore.GetVariable<string>("admin"));
    }

    [Fact]
    public async Task TheShapeAndTheResolverAgreeOnEveryValue()
    {
        // The two directions of one mapping, written in different files: StubShape puts a record into the
        // graph, this resolver reads one back out. If the shape published a value under a name the resolver
        // does not read - or the other way round - this is what says so.
        TimelineRun run = await Run(
            new PublishingEnvironment("payments", "http://127.0.0.1:49812/", adminPath: "/admin-api"),
            configured: [("Stub:payments:BaseUrl", "http://localhost:8080/"), ("Stub:payments:AdminPath", "/__admin")]);

        run.EnsureRanToCompletion();

        Assert.Equal("http://127.0.0.1:49812/", run.VariableStore.GetVariable<string>("address"));
        Assert.Equal("/admin-api", run.VariableStore.GetVariable<string>("admin"));
    }

    [Fact]
    public async Task AStubTheRunStartedNeedsNoConfigurationEntryAtAll()
    {
        // The case that broke the container suite the first time this landed, and it is the common one: a
        // stub declared only as a container definition has no entry in any file, because its address does not
        // exist until it starts. The old store write invented an entry on its way past; the run's resources
        // are where that belongs.
        TimelineRun run = await Run(new PublishingEnvironment("payments", "http://127.0.0.1:49812/", adminPath: "/__admin"));

        run.EnsureRanToCompletion();

        Assert.Equal("http://127.0.0.1:49812/", run.VariableStore.GetVariable<string>("address"));
        Assert.Equal("/__admin", run.VariableStore.GetVariable<string>("admin"));
    }

    [Fact]
    public async Task AStubNobodyDeclaredAndNothingStartedIsStillARefusal()
    {
        // The other half of the same change: dropping the "an entry must exist" rule must not turn a
        // misspelt identifier into a stub with no address.
        TimelineRun run = await Run(environment: null, resolves: "checkout", configured: [("Stub:payments:BaseUrl", "http://localhost:8080/")]);

        TimelineRunFailedException failure = Assert.Throws<TimelineRunFailedException>(run.EnsureRanToCompletion);

        // Names the one that does exist, so a typo is a sentence rather than a hunt.
        Assert.Contains("payments", failure.ToString(), StringComparison.Ordinal);
    }

    private async Task<TimelineRun> Run(PublishingEnvironment? environment, string resolves = "payments", params (string Key, string Value)[] configured)
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new ResolvesStubStep(resolves)).Name("resolves")
            .Build();

        ITimelineRunBuilder builder = timeline.SetupRun(Configured(configured), output);

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
        services.LoadWebStubConfigs(configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>Stands in for a stub container: something that starts and then knows where it is.</summary>
    private sealed class PublishingEnvironment(string identifier, string baseUrl, string? adminPath = null) : EnvironmentProviderBase
    {
        private readonly EnvComponentIdentifier component = "stub";

        public override IReadOnlyCollection<EnvComponentIdentifier> ResolveComponents(
            IEnumerable<Core.Artifacts.ArtifactInstanceGeneric> artifacts,
            IEnumerable<EnvironmentRequirement> requirements)
        {
            this.AddComponent(new PublishingComponent(this.component, identifier, baseUrl, adminPath));

            return [this.component];
        }
    }

    private sealed class PublishingComponent(EnvComponentIdentifier id, string identifier, string baseUrl, string? adminPath) : EnvComponent
    {
        public override EnvComponentIdentifier Id => id;

        public override Task<object?> CreateAsync(IEnvironmentProvider environment, RunContext context)
        {
            EnvironmentResources resources = PublishOn(context);

            resources.Produce(WebEnvironmentResourceKinds.StubKind, identifier, ValueNames.BaseUrl, ResourceVantage.Host, baseUrl);

            if (adminPath is not null)
            {
                resources.Produce(WebEnvironmentResourceKinds.StubKind, identifier, WebEnvironmentResourceKinds.AdminPathValue, adminPath);
            }

            return Task.FromResult<object?>(null);
        }

        public override Task DeconstructAsync(object? state, IEnvironmentProvider environment, RunContext context)
            => Task.CompletedTask;
    }

    /// <summary>Resolves the stub the way every stub step in the package now does.</summary>
    private sealed class ResolvesStubStep(string identifier) : Step<EmptyStepResultContext>
    {
        public override string Name => "Resolves a stub";

        public override string Description => "Resolves a stub's configuration through the run.";

        public override bool DoesReturn => false;

        public override Step<EmptyStepResultContext> Clone() => new ResolvesStubStep(identifier).WithClonedOptions(this);

        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);

        public override void DeclareIO(StepIOContract contract)
        {
        }

        public override Task<EmptyStepResultContext?> Execute(RunContext context)
        {
            StubConfig resolved = StubConfigResolver.Resolve(context, identifier);

            context.Variables.SetVariable("address", resolved.BaseUrl);
            context.Variables.SetVariable("admin", resolved.AdminPath);

            return Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);
        }
    }
}
