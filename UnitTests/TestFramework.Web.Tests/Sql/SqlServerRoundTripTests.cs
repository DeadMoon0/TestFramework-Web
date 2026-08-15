using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestFramework.Config;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web.Extensions;
using TestFramework.Web.Sql.Artifacts;
using TestFramework.Web.Sql.Steps;
using TestFramework.Web.Sql.Steps.IsLive;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// Exercises the SQL surface against a real SQL Server.
/// </summary>
/// <remarks>
/// Excluded from the default run because it needs a server. Point
/// <c>TESTFRAMEWORK_WEB_SQL</c> at one and run with <c>--filter "Category=SqlServer"</c>. The
/// statement generation and mapping these tests exercise are already covered without a database in
/// the other SQL test classes; this is the confirmation that the composed statements really run.
/// </remarks>
[Trait("Category", "SqlServer")]
public class SqlServerRoundTripTests
{
    private const string ConnectionStringVariable = "TESTFRAMEWORK_WEB_SQL";
    private const string TableName = "TestFrameworkWebRoundTrip";

    public sealed class RoundTripRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable)
        ?? throw new InvalidOperationException($"Set {ConnectionStringVariable} to a SQL Server connection string to run these tests.");

    private static ConfigInstance CreateConfig()
        => ConfigInstance.Create()
            .OverrideConfig(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Sql:main:ConnectionString"] = ConnectionString,
            })
            .LoadWebConfig()
            .AddWebSqlModels(models => models.For<RoundTripRow>().Table(TableName).Key(x => x.Id))
            .Build();

    private static SqlScript CreateTableScript => SqlScript.FromText($"""
        IF OBJECT_ID('{TableName}', 'U') IS NULL
        CREATE TABLE [{TableName}] ([Id] INT NOT NULL PRIMARY KEY, [Name] NVARCHAR(200) NOT NULL, [Quantity] INT NOT NULL);
        """);

    [Fact]
    public async Task IsLive_ReachesTheConfiguredDatabase()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.IsLive("main", SqlAlivenessLevel.Database)).Name("live")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.SqlProbe("live").Select(probe => probe.Success).Should().Be(true);
    }

    [Fact]
    public async Task SeededRow_IsInsertedReadBackAndRemoved()
    {
        int id = Random.Shared.Next(100_000, 999_999);

        Timeline seed = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", CreateTableScript)).Name("schema")
            .SetupArtifact("row")
            .FindArtifact("found", WebExt.ArtifactFinder.Sql.Where<RoundTripRow>("main", "Id = @id")
                .WithParameter("id", Var.Const(id)))
            .Build();

        TimelineRun run = await seed.SetupRun(CreateConfig())
            .AddArtifact(
                "row",
                WebExt.Artifact.Sql.Row<RoundTripRow>("main", Var.Const(id.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                new SqlRowArtifactData<RoundTripRow>(new RoundTripRow { Id = id, Name = "round-trip", Quantity = 5 }))
            .RunAsync();

        run.EnsureRanToCompletion();
        run.SqlRow<RoundTripRow>("found").Select(row => row.Name).Should().Be("round-trip");

        // The seeded row is owned by the run, so teardown removed it.
        Timeline verify = Timeline.Create()
            .Trigger(WebExt.Sql.Scalar<int>("main", $"SELECT COUNT(1) FROM [{TableName}] WHERE Id = @id")
                .WithParameter("id", Var.Const(id))).Name("count")
            .Build();

        TimelineRun verifyRun = await verify.SetupRun(CreateConfig()).RunAsync();

        verifyRun.EnsureRanToCompletion();
        verifyRun.SqlScalar<int>("count").Should().Be(0);
    }

    [Fact]
    public async Task ScriptBatches_ShareOneConnection()
    {
        // A #temp table lives on the connection. If each GO batch opened its own, the second batch
        // would fail with "Invalid object name '#seed'" - and pooling cannot rescue it, because the
        // pool issues sp_reset_connection when a connection goes back.
        SqlScript script = SqlScript.FromText("""
            CREATE TABLE #seed ([Value] INT NOT NULL);
            INSERT INTO #seed ([Value]) VALUES (41);
            GO
            UPDATE #seed SET [Value] = [Value] + 1;
            GO
            SELECT [Value] FROM #seed;
            """);

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", script)).Name("script")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.Step("script").Should().HaveCompleted();
    }

    [Fact]
    public async Task SeededRow_IsUpsertedWhenTheRowAlreadyExists()
    {
        int id = Random.Shared.Next(100_000, 999_999);

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", CreateTableScript)).Name("schema")
            .Trigger(WebExt.Sql.Execute("main", $"DELETE FROM [{TableName}] WHERE Id = @id; INSERT INTO [{TableName}] (Id, Name, Quantity) VALUES (@id, 'stale', 1);")
                .WithParameter("id", Var.Const(id))).Name("preexisting")
            .SetupArtifact("row")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig())
            .AddArtifact(
                "row",
                WebExt.Artifact.Sql.Row<RoundTripRow>("main", Var.Const(id.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                new SqlRowArtifactData<RoundTripRow>(new RoundTripRow { Id = id, Name = "fresh", Quantity = 9 }))
            .RunAsync();

        run.EnsureRanToCompletion();
        run.SqlRow<RoundTripRow>("row").Select(row => row.Name).Should().Be("fresh");
    }
}
