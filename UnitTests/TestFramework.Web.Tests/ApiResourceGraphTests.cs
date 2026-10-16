using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Web.Configuration;
using TestFramework.Web.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace TestFramework.Web.Tests;

/// <summary>
/// A configured API as the run's resource graph sees it.
/// </summary>
/// <remarks>
/// <para>
/// The first package in the family to describe its configuration as resources rather than only as its own
/// records. What it buys is the thing a step used to have to work out for itself: where an API is. That answer
/// used to live in whichever store happened to hold it, so anything wanting an address had to know somebody
/// else's configuration types and which of them won - a file, or a container that started later. Here a step
/// asks the run and never learns who answered.
/// </para>
/// <para>
/// The section is read once, by the same provider the package's own loader uses, so the store and the graph
/// cannot come to different conclusions about what the file said.
/// </para>
/// </remarks>
public class ApiResourceGraphTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AStepReadsAConfiguredApisAddressFromTheRun()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new ReadsValueStep(WebResourceKinds.RestApi, "orders", ValueNames.BaseUrl)).Label("reads")
            .Build();

        TimelineRun run = await timeline.SetupRun(Configured(("Api:orders:BaseUrl", "http://localhost:5080/")), output.WriteLine).RunAsync();

        run.EnsureRanToCompletion();

        Assert.Equal("http://localhost:5080/", run.VariableStore.GetVariable<string>("address"));
    }

    [Fact]
    public async Task AWrittenAddressAnswersFromBothViewpoints()
    {
        // A person writing one address wrote the address the thing answers on. Publishing it for the host
        // only would leave anything reading from inside the network with nothing, for no reason.
        Timeline timeline = Timeline.Create()
            .Trigger(new ReadsValueStep(WebResourceKinds.RestApi, "orders", ValueNames.BaseUrl, ResourceVantage.Network)).Label("reads")
            .Build();

        TimelineRun run = await timeline.SetupRun(Configured(("Api:orders:BaseUrl", "http://localhost:5080/")), output.WriteLine).RunAsync();

        run.EnsureRanToCompletion();

        Assert.Equal("http://localhost:5080/", run.VariableStore.GetVariable<string>("address"));
    }

    [Fact]
    public async Task TheStoreAndTheGraphReadTheSameFile()
    {
        // One reader, so the two cannot disagree about what the entry said. The store is what the package's
        // own steps use; the graph is what anything else uses.
        IServiceProvider services = Configured(("Api:orders:BaseUrl", "http://localhost:5080/"));

        Timeline timeline = Timeline.Create()
            .Trigger(new ReadsValueStep(WebResourceKinds.RestApi, "orders", ValueNames.BaseUrl)).Label("reads")
            .Build();

        TimelineRun run = await timeline.SetupRun(services, output.WriteLine).RunAsync();

        run.EnsureRanToCompletion();

        // Read back the same way the step did, so this asserts a round trip rather than a store lookup.
        Assert.Equal(
            RunContext.Detached(services).Configured<ApiConfig>("orders").BaseUrl,
            run.VariableStore.GetVariable<string>("address"));
    }

    [Fact]
    public async Task AStepReadsAConfiguredSitesAddressFromTheRun()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new ReadsValueStep(WebResourceKinds.Site, "storefront", ValueNames.BaseUrl)).Label("reads")
            .Build();

        TimelineRun run = await timeline.SetupRun(Configured(("Site:storefront:BaseUrl", "http://localhost:5090/")), output.WriteLine).RunAsync();

        run.EnsureRanToCompletion();

        Assert.Equal("http://localhost:5090/", run.VariableStore.GetVariable<string>("address"));
    }

    [Fact]
    public async Task AStepReadsAConfiguredStubsAddressAndAdminPath()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new ReadsValueStep(WebResourceKinds.Stub, "payments", ValueNames.BaseUrl)).Label("address")
            .Trigger(new ReadsValueStep(WebResourceKinds.Stub, "payments", WebResourceKinds.AdminPathValue)).Label("admin")
            .Build();

        TimelineRun run = await timeline
            .SetupRun(Configured(("Stub:payments:BaseUrl", "http://localhost:5091/"), ("Stub:payments:AdminPath", "/__admin")), output.WriteLine)
            .RunAsync();

        run.EnsureRanToCompletion();

        // The last step wins the variable; both ran, which is what EnsureRanToCompletion just checked.
        Assert.Equal("/__admin", run.VariableStore.GetVariable<string>("address"));
    }

    [Fact]
    public async Task AStepReadsAConfiguredDatabasesConnectionString()
    {
        // Composed from the parts by the package's own factory rather than by the shape, so the graph and
        // anything that connects agree on the spelling.
        Timeline timeline = Timeline.Create()
            .Trigger(new ReadsValueStep(WebResourceKinds.Sql, "orders-db", ValueNames.ConnectionString)).Label("reads")
            .Build();

        TimelineRun run = await timeline
            .SetupRun(
                Configured(
                    ("Sql:orders-db:Server", "localhost,14333"),
                    ("Sql:orders-db:Database", "orders"),
                    ("Sql:orders-db:IntegratedSecurity", "true")),
                output.WriteLine)
            .RunAsync();

        run.EnsureRanToCompletion();

        string connectionString = run.VariableStore.GetVariable<string>("address")!;

        Assert.Contains("localhost,14333", connectionString, StringComparison.Ordinal);
        Assert.Contains("orders", connectionString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADatabaseAlsoPublishesItsName()
    {
        // A name is one value for every viewpoint, unlike the coordinate: "orders" is what the database is
        // called from wherever you are looking at it.
        Timeline timeline = Timeline.Create()
            .Trigger(new ReadsValueStep(WebResourceKinds.Sql, "orders-db", WebResourceKinds.DatabaseNameValue)).Label("reads")
            .Build();

        TimelineRun run = await timeline
            .SetupRun(
                Configured(
                    ("Sql:orders-db:Server", "localhost,14333"),
                    ("Sql:orders-db:Database", "orders"),
                    ("Sql:orders-db:IntegratedSecurity", "true")),
                output.WriteLine)
            .RunAsync();

        run.EnsureRanToCompletion();

        Assert.Equal("orders", run.VariableStore.GetVariable<string>("address"));
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
        // All four sections, because a run that configures a site and a stub loads both - and the point of the
        // graph is that a reader does not care which section an identifier came from.
        services.AddWeb(configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>Asks the run about a resource, rather than asking a configuration store.</summary>
    private sealed class ReadsValueStep(string kind, string identifier, string valueName, ResourceVantage? vantage = null)
        : Step<EmptyStepResultContext>
    {
        public override string Name => "Reads a resource value";

        public override string Description => "Reads a configured resource's value from the run's resources.";

        public override bool DoesReturn => false;

        public override Step<EmptyStepResultContext> Clone() => new ReadsValueStep(kind, identifier, valueName, vantage).WithClonedOptions(this);

        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);

        public override void DeclareIO(StepIOContract contract)
        {
        }

        public override Task<EmptyStepResultContext?> Execute(RunContext context)
        {
            context.Variables.SetVariable(
                "address",
                context.Values.Require(ValueRef.For(kind, identifier, valueName), vantage ?? ResourceVantage.Host));

            return Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);
        }
    }
}
