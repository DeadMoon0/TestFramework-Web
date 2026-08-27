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
using TestFramework.Web.Stub.Exceptions;
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

    [Fact]
    public async Task ASettingTravelsThroughTheRunAsWellAsAnAddress()
    {
        // The whole entry now, not only its coordinates. A poll interval and a reset mode are declared in a
        // file, read into resource values when the run composes them, and turned back into a typed record -
        // so nothing asks a service provider for the configuration a run was set up with.
        TimelineRun run = await Run(
            environment: null,
            configured:
            [
                ("Stub:payments:BaseUrl", "http://localhost:8080/"),
                ("Stub:payments:PollInterval", "00:00:00.750"),
                ("Stub:payments:ResetMode", "ClearServerLog"),
                ("Stub:payments:AllowInvalidCertificates", "true"),
            ]);

        run.EnsureRanToCompletion();

        Assert.Equal(TimeSpan.FromMilliseconds(750), run.VariableStore.GetVariable<TimeSpan>("poll"));
        Assert.Equal(StubResetMode.ClearServerLog, run.VariableStore.GetVariable<StubResetMode>("reset"));
        Assert.True(run.VariableStore.GetVariable<bool>("allow-invalid"));
    }

    [Fact]
    public async Task AbsentSettingsLeaveTheRecordsOwnDefaults()
    {
        // §5: a default is documented where the value is introduced, so rebuilding a record must not
        // restate one. An entry that says nothing about polling gets the record's answer, not the shape's.
        TimelineRun run = await Run(environment: null, configured: [("Stub:payments:BaseUrl", "http://localhost:8080/")]);

        run.EnsureRanToCompletion();

        Assert.Equal(new StubConfig { BaseUrl = "x" }.PollInterval, run.VariableStore.GetVariable<TimeSpan>("poll"));
        Assert.Equal(new StubConfig { BaseUrl = "x" }.ResetMode, run.VariableStore.GetVariable<StubResetMode>("reset"));
        Assert.Equal("/__admin", run.VariableStore.GetVariable<string>("admin"));
    }

    [Fact]
    public async Task SomethingThatStartedCanOverrideASettingAndNotOnlyAnAddress()
    {
        // New capability, and the reason one channel is worth having: a stub image that knows its own
        // minimum poll interval can say so, and a run believes the thing that is running over the file that
        // guessed. Impossible while settings lived in a store only a person wrote.
        TimelineRun run = await Run(
            new PublishingEnvironment(
                "payments",
                "http://127.0.0.1:49812/",
                settings: [(WebEnvironmentResourceKinds.PollIntervalValue, "00:00:02")]),
            configured: [("Stub:payments:BaseUrl", "http://localhost:8080/"), ("Stub:payments:PollInterval", "00:00:00.250")]);

        run.EnsureRanToCompletion();

        Assert.Equal(TimeSpan.FromSeconds(2), run.VariableStore.GetVariable<TimeSpan>("poll"));
    }

    [Fact]
    public async Task AValueThatCannotBeReadIsRefusedRatherThanDefaulted()
    {
        // Published by something that started, not written in a file - because a file cannot get here. The
        // package's own provider refuses an unusable entry while *loading*, with a better message than this
        // one, and that validation is untouched by configuration moving onto the run. What this guards is
        // the other road in: a component publishing something nothing validated.
        //
        // Falling back to the default would mean a run behaving differently from what it was told with
        // nothing to show why.
        TimelineRun run = await Run(
            new PublishingEnvironment(
                "payments",
                "http://127.0.0.1:49812/",
                settings: [(WebEnvironmentResourceKinds.PollIntervalValue, "soon")]),
            configured: [("Stub:payments:BaseUrl", "http://localhost:8080/")]);

        TimelineRunFailedException failure = Assert.Throws<TimelineRunFailedException>(run.EnsureRanToCompletion);

        Assert.Contains("PollInterval", failure.ToString(), StringComparison.Ordinal);
        Assert.Contains("soon", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnusableEntryIsStillRefusedWhileLoading()
    {
        // The half that did not move. Configuration reaching a step through the run changes where the record
        // is built, not who validates the file - and this is the load-time refusal proving it, because a
        // value this bad never reaches the shape's reader at all.
        StubConfigurationValidationException failure = Assert.Throws<StubConfigurationValidationException>(
            () => Configured(("Stub:payments:BaseUrl", "http://localhost:8080/"), ("Stub:payments:PollInterval", "soon")));

        Assert.Contains("PollInterval", failure.Message, StringComparison.Ordinal);
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
    private sealed class PublishingEnvironment(string identifier, string baseUrl, string? adminPath = null, (string Name, string Value)[]? settings = null) : EnvironmentProviderBase
    {
        private readonly EnvComponentIdentifier component = "stub";

        public override IReadOnlyCollection<EnvComponentIdentifier> ResolveComponents(
            IEnumerable<Core.Artifacts.ArtifactInstanceGeneric> artifacts,
            IEnumerable<EnvironmentRequirement> requirements)
        {
            this.AddComponent(new PublishingComponent(this.component, identifier, baseUrl, adminPath, settings));

            return [this.component];
        }
    }

    private sealed class PublishingComponent(EnvComponentIdentifier id, string identifier, string baseUrl, string? adminPath, (string Name, string Value)[]? settings = null) : EnvComponent
    {
        public override EnvComponentIdentifier Id => id;

        public override Task<object?> CreateAsync(IEnvironmentProvider environment, RunContext context)
        {
            EnvironmentResources resources = PublishOn(context);

            foreach ((string name, string value) in settings ?? [])
            {
                resources.Produce(WebEnvironmentResourceKinds.StubKind, identifier, name, value);
            }

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
            StubConfig resolved = context.Configured<StubConfig>(identifier);

            context.Variables.SetVariable("address", resolved.BaseUrl);
            context.Variables.SetVariable("admin", resolved.AdminPath);
            context.Variables.SetVariable("poll", resolved.PollInterval);
            context.Variables.SetVariable("reset", resolved.ResetMode);
            context.Variables.SetVariable("allow-invalid", resolved.AllowInvalidCertificates);

            return Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);
        }
    }
}
