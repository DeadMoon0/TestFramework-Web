using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config;
using TestFramework.Core.Environment;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Builder.TimelineRunBuilder;
using TestFramework.Web.Extensions;
using TestFramework.Web.Sql;
using Xunit;
using Xunit.Abstractions;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// Where a database is, when a container knows and the file does not.
/// </summary>
/// <remarks>
/// <para>
/// The last of Web's three resources to move onto the run, and the one whose seam looked like it would have
/// to change. It did not: <see cref="Execution.ISqlExecutor"/> keeps every signature, because the executor is
/// built per run and holds it rather than being handed a run per call. An implementation somebody else wrote
/// is unaffected.
/// </para>
/// <para>
/// Same five questions asked of the stub and the API, so the three stay comparable.
/// </para>
/// </remarks>
public class SqlResourceTests(ITestOutputHelper output)
{
    private const string Declared = "Server=localhost,14333;Database=orders;Integrated Security=True";

    [Fact]
    public async Task WhatAContainerPublishesWinsOverWhatTheFileSaid()
    {
        TimelineRun run = await Run(
            new PublishingEnvironment("orders-db", "Server=127.0.0.1,49999;Database=orders;User Id=sa;Password=x"),
            configured: [("Sql:orders-db:Server", "localhost,14333"), ("Sql:orders-db:Database", "orders"), ("Sql:orders-db:IntegratedSecurity", "true")]);

        run.EnsureRanToCompletion();

        Assert.Equal("Server=127.0.0.1,49999;Database=orders;User Id=sa;Password=x", run.VariableStore.GetVariable<string>("connection"));

        // Not published, so it still comes from the declaration - a produced value replaces one value, not
        // the entry.
        Assert.Equal("orders", run.VariableStore.GetVariable<string>("database"));
    }

    [Fact]
    public async Task WithNothingPublishedTheDeclarationStillAnswers()
    {
        // A deployed database: the file is the only source, and reading through the run has to work exactly
        // as well as reading the store did.
        TimelineRun run = await Run(
            environment: null,
            configured: [("Sql:orders-db:Server", "localhost,14333"), ("Sql:orders-db:Database", "orders"), ("Sql:orders-db:IntegratedSecurity", "true")]);

        run.EnsureRanToCompletion();

        // The graph carries the *composed* coordinate, because SqlShape asks the package's own factory what
        // the entry amounts to. So a reader gets one connection string whether the file spelled one out or
        // gave a server and a database - which is the point of asking the run rather than reading the record.
        // The record's own ConnectionString was null here; this is the graph's declared value overlaid on it.
        string connection = run.VariableStore.GetVariable<string>("connection")!;

        Assert.Contains("localhost,14333", connection, StringComparison.Ordinal);
        Assert.Contains("orders", connection, StringComparison.Ordinal);

        // Integrated security came off the entry too, which is the other half of the point: the composition
        // is the package's own, so the graph carries what a connection would actually have been opened with.
        Assert.Contains("Integrated Security=True", connection, StringComparison.Ordinal);

        Assert.Equal("orders", run.VariableStore.GetVariable<string>("database"));
    }

    [Fact]
    public async Task TheShapeAndTheResolverAgreeOnEveryValue()
    {
        // The two directions of one mapping, written in different files: SqlShape composes an entry into the
        // graph, SqlConfigResolver reads one back out.
        TimelineRun run = await Run(
            new PublishingEnvironment("orders-db", "Server=127.0.0.1,49999;Database=shadow", database: "shadow"),
            configured: [("Sql:orders-db:Server", "localhost,14333"), ("Sql:orders-db:Database", "orders"), ("Sql:orders-db:IntegratedSecurity", "true")]);

        run.EnsureRanToCompletion();

        Assert.Equal("Server=127.0.0.1,49999;Database=shadow", run.VariableStore.GetVariable<string>("connection"));
        Assert.Equal("shadow", run.VariableStore.GetVariable<string>("database"));
    }

    [Fact]
    public async Task ADatabaseTheRunStartedNeedsNoConfigurationEntryAtAll()
    {
        // Entry 21's case: a container-only database needs no entry at all, and a produced connection
        // string is complete on its own.
        TimelineRun run = await Run(new PublishingEnvironment("orders-db", "Server=127.0.0.1,49999;Database=orders", database: "orders"));

        run.EnsureRanToCompletion();

        Assert.Equal("Server=127.0.0.1,49999;Database=orders", run.VariableStore.GetVariable<string>("connection"));
        Assert.Equal("orders", run.VariableStore.GetVariable<string>("database"));
    }

    [Fact]
    public async Task ANameOnlyEntryDeclaresTheNameAndCrashesNothing()
    {
        // The other half of entry 21, and a regression its tests did not cover: the provider deliberately
        // accepts an entry that only names the database, so the shape meets one while composing the graph.
        // Its "does this entry have coordinates" guard caught the wrong exception type, so this exact run -
        // a name in the file, the address published by the container - died at composition instead of
        // publishing the name and letting the produced connection answer.
        TimelineRun run = await Run(
            new PublishingEnvironment("orders-db", "Server=127.0.0.1,49999;Database=orders"),
            configured: [("Sql:orders-db:Database", "orders")]);

        run.EnsureRanToCompletion();

        Assert.Equal("Server=127.0.0.1,49999;Database=orders", run.VariableStore.GetVariable<string>("connection"));

        // The name came from the file - the one thing the entry said, published as declared.
        Assert.Equal("orders", run.VariableStore.GetVariable<string>("database"));
    }

    [Fact]
    public async Task ADatabaseNobodyDeclaredAndNothingStartedIsStillARefusal()
    {
        TimelineRun run = await Run(
            environment: null,
            resolves: "invoices-db",
            configured: [("Sql:orders-db:Server", "localhost,14333"), ("Sql:orders-db:Database", "orders"), ("Sql:orders-db:IntegratedSecurity", "true")]);

        TimelineRunFailedException failure = Assert.Throws<TimelineRunFailedException>(run.EnsureRanToCompletion);

        // Names the one that does exist, so a typo is a sentence rather than a hunt.
        Assert.Contains("orders-db", failure.ToString(), StringComparison.Ordinal);
    }

    private async Task<TimelineRun> Run(PublishingEnvironment? environment, string resolves = "orders-db", params (string Key, string Value)[] configured)
    {
        Timeline timeline = Timeline.Create()
            .Trigger(new ResolvesDatabaseStep(resolves)).Label("resolves")
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
        services.AddWeb(configuration);

        return services.BuildServiceProvider();
    }

    /// <summary>Stands in for a database container: it starts, then it knows its port.</summary>
    private sealed class PublishingEnvironment(string identifier, string connectionString, string? database = null) : EnvironmentProviderBase
    {
        private readonly EnvComponentIdentifier component = "sql";

        public override IReadOnlyCollection<EnvComponentIdentifier> ResolveComponents(
            IEnumerable<Core.Artifacts.ArtifactInstanceGeneric> artifacts,
            IEnumerable<EnvironmentRequirement> requirements)
        {
            this.AddComponent(new PublishingComponent(this.component, identifier, connectionString, database));

            return [this.component];
        }
    }

    private sealed class PublishingComponent(EnvComponentIdentifier id, string identifier, string connectionString, string? database) : EnvComponent
    {
        public override EnvComponentIdentifier Id => id;

        public override Task<object?> CreateAsync(IEnvironmentProvider environment, RunContext context)
        {
            EnvironmentResources resources = PublishOn(context);

            resources.Produce(WebEnvironmentResourceKinds.SqlKind, identifier, ValueNames.ConnectionString, ResourceVantage.Host, connectionString);
            resources.Produce(WebEnvironmentResourceKinds.SqlKind, identifier, ValueNames.ConnectionString, ResourceVantage.Network, "Server=sql,1433;Database=orders");

            if (database is not null)
            {
                resources.Produce(WebEnvironmentResourceKinds.SqlKind, identifier, WebEnvironmentResourceKinds.DatabaseNameValue, database);
            }

            return Task.FromResult<object?>(null);
        }

        public override Task DeconstructAsync(object? state, IEnvironmentProvider environment, RunContext context)
            => Task.CompletedTask;
    }

    /// <summary>Resolves the database the way every SQL step and executor now does.</summary>
    private sealed class ResolvesDatabaseStep(string identifier) : Step<EmptyStepResultContext>
    {
        public override string Name => "Resolves a database";

        public override string Description => "Resolves a database's configuration through the run.";

        public override bool DoesReturn => false;

        public override Step<EmptyStepResultContext> Clone() => new ResolvesDatabaseStep(identifier).WithClonedOptions(this);

        public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);

        public override void DeclareIO(StepIOContract contract)
        {
        }

        public override Task<EmptyStepResultContext?> Execute(RunContext context)
        {
            SqlConfig resolved = context.Configured<SqlConfig>(identifier);

            context.Variables.SetVariable("connection", resolved.ConnectionString);
            context.Variables.SetVariable("database", resolved.Database);
            context.Variables.SetVariable("command-timeout", resolved.CommandTimeout);

            return Task.FromResult<EmptyStepResultContext?>(EmptyStepResultContext.Instance);
        }
    }

    [Fact]
    public async Task AProducedConnectionStringIsTakenWholeAndNotQualifiedByTheEntry()
    {
        // The case the store write used to handle by clearing fields, then an origin-aware read, and now
        // nothing at all: a container owns its port and its credentials, and the entry describes reaching a
        // database somewhere else. Integrated security applied on top of the container's own connection
        // string would strip out the user and password it just handed over.
        //
        // What changed is that there is no longer a decision to get right. The entry's server, user and
        // integrated-security flag are ingredients composed into *its* connection string before publishing,
        // so they are never separate values - and a produced string simply wins the one slot they share.
        TimelineRun run = await Run(
            new PublishingEnvironment("orders-db", "Server=127.0.0.1,49999;Database=orders;User Id=sa;Password=secret"),
            configured: [("Sql:orders-db:Server", "localhost,14333"), ("Sql:orders-db:Database", "orders"), ("Sql:orders-db:IntegratedSecurity", "true")]);

        run.EnsureRanToCompletion();

        string connection = run.VariableStore.GetVariable<string>("connection")!;

        Assert.Equal("Server=127.0.0.1,49999;Database=orders;User Id=sa;Password=secret", connection);

        // Nothing of the declared way of connecting survived into it, which is what a produced coordinate
        // being "complete" means. A declared server left in place is how a run silently reaches the wrong
        // database.
        Assert.DoesNotContain("localhost,14333", connection, StringComparison.Ordinal);
        Assert.DoesNotContain("Integrated Security", connection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeclaredEntrysIngredientsEndUpInsideItsConnectionString()
    {
        // The other side of the same rule, and it reads differently now. A server and an integrated-security
        // flag are not values a reader gets back - they are ingredients this package's own factory composes
        // into the connection string before it is published. So "every part of the entry still applies" is
        // visible in the string rather than in six properties beside it.
        TimelineRun run = await Run(
            environment: null,
            configured: [("Sql:orders-db:Server", "localhost,14333"), ("Sql:orders-db:Database", "orders"), ("Sql:orders-db:IntegratedSecurity", "true")]);

        run.EnsureRanToCompletion();

        string connection = run.VariableStore.GetVariable<string>("connection")!;

        Assert.Contains("localhost,14333", connection, StringComparison.Ordinal);
        Assert.Contains("Integrated Security=True", connection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACommandTimeoutTravelsBesideTheConnectionString()
    {
        // The one property that is genuinely not inside the string: it bounds a command rather than a
        // connection, so it would be lost if the string were the only thing carried.
        TimelineRun run = await Run(
            environment: null,
            configured:
            [
                ("Sql:orders-db:Server", "localhost,14333"),
                ("Sql:orders-db:Database", "orders"),
                ("Sql:orders-db:IntegratedSecurity", "true"),
                ("Sql:orders-db:CommandTimeout", "00:01:30"),
            ]);

        run.EnsureRanToCompletion();

        Assert.Equal(TimeSpan.FromSeconds(90), run.VariableStore.GetVariable<TimeSpan?>("command-timeout"));
    }

    [Fact]
    public async Task AConnectionStringIsNeverPrintedInTheListing()
    {
        // The only value in the family that is an address and a credential in one indivisible text - so it
        // has to travel, and a listing must never show it.
        TimelineRun run = await Run(
            environment: null,
            configured: [("Sql:orders-db:Server", "localhost,14333"), ("Sql:orders-db:Database", "orders"), ("Sql:orders-db:UserName", "sa"), ("Sql:orders-db:Password", "hunter2")]);

        run.EnsureRanToCompletion();

        Assert.Contains("hunter2", run.VariableStore.GetVariable<string>("connection")!, StringComparison.Ordinal);

        string listed = string.Join(" ", run.Values.Snapshot().Select(static value => $"{value.Key}={value.Value}"));

        Assert.DoesNotContain("hunter2", listed, StringComparison.Ordinal);
        Assert.Contains("ConnectionString (Host)=(secret)", listed, StringComparison.Ordinal);

        // The database name is not a credential and stays readable, so a failure can still say which one.
        Assert.Contains("orders", listed, StringComparison.Ordinal);
    }
}
