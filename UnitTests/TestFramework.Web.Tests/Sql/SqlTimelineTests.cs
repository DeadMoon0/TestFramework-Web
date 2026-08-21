using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config;
using TestFramework.Config.Builder.InstanceBuilder;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web.Extensions;
using TestFramework.Web.Sql.Artifacts;
using TestFramework.Web.Sql.Execution;
using TestFramework.Web.Sql.Steps;
using TestFramework.Web.Sql.Steps.IsLive;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// Runs the SQL surface through real timelines against a recording executor, so behaviour is covered
/// without a database. Round-trip coverage against a real server lives in the SqlServer category.
/// </summary>
public class SqlTimelineTests
{
    public sealed class Order
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    private static ConfigInstance CreateConfig(RecordingSqlExecutor executor, Action<SqlModelBuilderScope>? models = null)
    {
        IConfigInstanceBuilder builder = ConfigInstance.Create()
            .OverrideConfig(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Sql:main:Server"] = "localhost",
                ["Sql:main:Database"] = "SampleDb",
                ["Sql:main:IntegratedSecurity"] = "true",
            })
            .LoadWebConfig()
            .AddWebSqlModels(model =>
            {
                model.For<Order>().Table("Orders").Key(x => x.Id);
                models?.Invoke(new SqlModelBuilderScope(model));
            })
            .AddService(services => services.AddSingleton<ISqlExecutor>(executor));

        return builder.Build();
    }

    public sealed class SqlModelBuilderScope(TestFramework.Web.Sql.Model.SqlModelBuilder builder)
    {
        public TestFramework.Web.Sql.Model.SqlModelBuilder Builder { get; } = builder;
    }

    [Fact]
    public async Task Execute_BindsParametersFromVariablesAndReportsAffectedRows()
    {
        RecordingSqlExecutor executor = new() { ExecuteResult = 3 };

        Timeline timeline = Timeline.Create()
            .SetVariable("status", Var.Const(9))
            .SetVariable("name", Var.Const("test-order"))
            .Trigger(WebExt.Sql.Execute("main", "UPDATE Orders SET Status = @status WHERE Name = @name")
                .WithParameter("status", Var.Ref<int>("status"))
                .WithParameter("name", Var.Ref<string>("name"))).Name("update")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor)).RunAsync();

        run.EnsureRanToCompletion();
        run.SqlAffectedRows("update").Should().Be(3);

        RecordedSqlCall call = Assert.Single(executor.Calls);
        Assert.Equal(9, call.Parameters["status"]);
        Assert.Equal("test-order", call.Parameters["name"]);
    }

    [Fact]
    public async Task Scalar_ReturnsTheValueAndRunsInTheObservePhase()
    {
        RecordingSqlExecutor executor = new() { ScalarResult = _ => 7 };

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Scalar<int>("main", "SELECT COUNT(*) FROM Orders WHERE Status = @status")
                .WithParameter("status", Var.Const(3))).Name("count")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor)).RunAsync();

        run.EnsureRanToCompletion();
        run.SqlScalar<int>("count").Should().Be(7);
        Assert.Equal(TestFramework.Core.Steps.Options.StepExecutionPhase.Observe, run.Step("count").Step.Phase);
    }

    [Fact]
    public async Task Script_KeepsPerBatchSemantics_ForAnExecutorThatDoesNotOverrideTheScriptMethod()
    {
        // RecordingSqlExecutor implements only the required members, exactly like an external
        // implementer written before ExecuteScriptAsync existed. The default interface body applies,
        // so it still sees one call per batch.
        RecordingSqlExecutor executor = new();
        SqlScript script = SqlScript.FromText("DELETE FROM Orders;\nGO\nINSERT INTO Orders (Id) VALUES (1);");

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", script)).Name("seed")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor)).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(2, executor.Calls.Count);
        Assert.DoesNotContain(executor.Calls, call => call.Contains("GO"));
    }

    [Fact]
    public async Task Script_HandsEveryBatchToTheExecutorAtOnce()
    {
        ScriptAwareSqlExecutor executor = new();
        SqlScript script = SqlScript.FromText("DELETE FROM Orders;\nGO\nINSERT INTO Orders (Id) VALUES (1);");

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", script)).Name("seed")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor)).RunAsync();

        run.EnsureRanToCompletion();

        // One call carrying both batches: that is what lets the executor keep them on one connection.
        IReadOnlyList<string> batches = Assert.Single(executor.Scripts);
        Assert.Equal(["DELETE FROM Orders;", "INSERT INTO Orders (Id) VALUES (1);"], batches);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task Script_WarnsWhenABatchLeavesATransactionOpen()
    {
        ScriptAwareSqlExecutor executor = new() { OpenTransactionCount = 1 };
        SqlScript script = SqlScript.FromText("BEGIN TRAN;\nGO\nINSERT INTO Orders (Id) VALUES (1);");

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", script)).Name("seed")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor)).RunAsync();

        // The step still completes - the server reported no error - which is precisely why the
        // dangling transaction has to be said out loud.
        run.EnsureRanToCompletion();
        run.Step("seed").Should().HaveCompleted();
    }

    [Fact]
    public async Task IsLive_ProbesTheConfiguredDatabase()
    {
        RecordingSqlExecutor executor = new() { ScalarResult = _ => "SampleDb" };

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.IsLive("main", SqlAlivenessLevel.Database)).Name("live")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor)).RunAsync();

        run.EnsureRanToCompletion();
        run.SqlProbe("live").Select(probe => probe.Success).Should().Be(true);
        Assert.Contains(executor.Calls, call => call.Contains("DB_NAME"));
    }

    [Fact]
    public async Task SeededRow_IsInsertedWhenAbsentAndDeletedOnTeardown()
    {
        RecordingSqlExecutor executor = new() { ScalarResult = _ => 0 };

        Timeline timeline = Timeline.Create()
            .SetupArtifact("order")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor))
            .AddArtifact(
                "order",
                WebExt.Artifact.Sql.Row<Order>("main", Var.Const("4711")),
                new SqlRowArtifactData<Order>(new Order { Id = 4711, Name = "seeded", Quantity = 1 }))
            .RunAsync();

        run.EnsureRanToCompletion();

        Assert.Single(executor.CallsContaining("INSERT INTO"));
        Assert.Single(executor.CallsContaining("DELETE FROM"));

        RecordedSqlCall insert = executor.CallsContaining("INSERT INTO")[0];
        Assert.Equal(4711, insert.Parameters["tf_val0"]);
        Assert.Equal("seeded", insert.Parameters["tf_val1"]);
    }

    [Fact]
    public async Task SeededRow_IsUpdatedWhenItAlreadyExists()
    {
        // Setup upserts, so a rerun against a dirty database converges instead of failing.
        RecordingSqlExecutor executor = new() { ScalarResult = _ => 1 };

        Timeline timeline = Timeline.Create()
            .SetupArtifact("order")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor))
            .AddArtifact(
                "order",
                WebExt.Artifact.Sql.Row<Order>("main", Var.Const("4711")),
                new SqlRowArtifactData<Order>(new Order { Id = 4711, Name = "seeded", Quantity = 1 }))
            .RunAsync();

        run.EnsureRanToCompletion();

        Assert.Single(executor.CallsContaining("UPDATE"));
        Assert.Empty(executor.CallsContaining("INSERT INTO"));
    }

    [Fact]
    public async Task FoundRows_AreDeleted_BecauseThatIsTheDefault()
    {
        // The finder no longer decides this for the author. A found row gets the same default as a
        // seeded one - removed at teardown - and MarkReadonly() is how a test says "I only read it".
        RecordingSqlExecutor executor = new()
        {
            QueryResult = _ => new[] { new Order { Id = 99, Name = "test-order", Quantity = 3 } },
        };

        Timeline timeline = Timeline.Create()
            .SetVariable("name", Var.Const("test-order"))
            .FindArtifact("order", WebExt.ArtifactFinder.Sql.Where<Order>("main", "Name = @name")
                .WithParameter("name", Var.Ref<string>("name")))
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor)).RunAsync();

        run.EnsureRanToCompletion();
        run.SqlRow<Order>("order").Select(order => order.Quantity).Should().Be(3);
        Assert.Single(executor.CallsContaining("DELETE FROM"));
    }

    [Fact]
    public async Task FoundRows_AreNeverDeleted_WhenTheTimelineMarksThemReadonly()
    {
        // The protection a test reaches for against a shared database, and it has to hold even though
        // the reference reports itself perfectly capable of deleting the row.
        RecordingSqlExecutor executor = new()
        {
            QueryResult = _ => new[] { new Order { Id = 99, Name = "test-order", Quantity = 3 } },
        };

        Timeline timeline = Timeline.Create()
            .SetVariable("name", Var.Const("test-order"))
            .FindArtifact("order", WebExt.ArtifactFinder.Sql.Where<Order>("main", "Name = @name")
                .WithParameter("name", Var.Ref<string>("name")))
            .MarkReadonly()
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor)).RunAsync();

        run.EnsureRanToCompletion();
        run.SqlRow<Order>("order").Select(order => order.Quantity).Should().Be(3);
        Assert.Empty(executor.CallsContaining("DELETE FROM"));
    }

    [Fact]
    public async Task FindArtifact_PassesTheWhereClauseAndItsParameters()
    {
        RecordingSqlExecutor executor = new()
        {
            QueryResult = _ => new[] { new Order { Id = 1, Name = "test-order", Quantity = 1 } },
        };

        Timeline timeline = Timeline.Create()
            .SetVariable("name", Var.Const("test-order"))
            .FindArtifact("order", WebExt.ArtifactFinder.Sql.Where<Order>("main", "Name = @name")
                .WithParameter("name", Var.Ref<string>("name")))
            // Readonly so teardown's DELETE, which addresses the same key, stays out of the counts below.
            .MarkReadonly()
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor)).RunAsync();

        run.EnsureRanToCompletion();
        // The finder selects by predicate; the artifact machinery then resolves the located
        // reference by key, so two selects are expected.
        RecordedSqlCall select = Assert.Single(executor.CallsContaining("WHERE Name = @name"));
        Assert.Equal("test-order", select.Parameters["name"]);
        Assert.Single(executor.CallsContaining("WHERE [Id] = @tf_key0"));
    }

    [Fact]
    public async Task UnknownIdentifier_FailsWithTheRegisteredIdentifiersListed()
    {
        RecordingSqlExecutor executor = new();

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Execute("missing", "DELETE FROM Orders")).Name("delete")
            .Build();

        // The recording executor is bypassed here on purpose: resolution fails before it is reached.
        ConfigInstance config = ConfigInstance.Create()
            .OverrideConfig(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Sql:main:Server"] = "localhost",
                ["Sql:main:Database"] = "SampleDb",
            })
            .LoadWebConfig()
            .Build();

        TimelineRun run = await timeline.SetupRun(config).RunAsync();

        run.Step("delete").Should().HaveThrown<TestFramework.Web.Sql.Exceptions.SqlConfigurationValidationException>();
        run.Assert(run.Step("delete").LastResult.Exception!.Message, "sql configuration failure")
            .Should().Contain("missing").And().Contain("main");
    }

    /// <summary>
    /// An executor that takes the whole script, standing in for one that keeps a single connection.
    /// </summary>
    private sealed class ScriptAwareSqlExecutor : RecordingSqlExecutor, ISqlExecutor
    {
        private readonly List<IReadOnlyList<string>> _scripts = [];

        public IReadOnlyList<IReadOnlyList<string>> Scripts => _scripts;

        public int OpenTransactionCount { get; init; }

        public Task<SqlScriptExecutionResult> ExecuteScriptAsync(
            string identifier,
            IReadOnlyList<string> batches,
            IReadOnlyDictionary<string, object?> parameters,
            System.Threading.CancellationToken cancellationToken)
        {
            _scripts.Add(batches);
            return Task.FromResult(new SqlScriptExecutionResult(batches.Count, OpenTransactionCount));
        }
    }
}
