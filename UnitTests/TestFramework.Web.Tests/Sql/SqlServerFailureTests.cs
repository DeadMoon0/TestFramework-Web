using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using TestFramework.Config;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web.Extensions;
using TestFramework.Web.Sql.Exceptions;
using TestFramework.Web.Sql.Steps;
using TestFramework.Web.Tests.Shared;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// What the shipped executor does when a statement goes wrong, and when a test adopts a row it did not create -
/// against a real SQL Server, because these are the driver's answers and no recording executor gives them.
/// </summary>
/// <remarks>Needs <c>TESTFRAMEWORK_WEB_SQL</c>, like <see cref="SqlServerRoundTripTests"/>.</remarks>
[Trait("Category", "SqlServer")]
public class SqlServerFailureTests
{
    private const string TableName = "TestFrameworkWebAdopted";

    public sealed class AdoptedRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static ConfigInstance CreateConfig(string? commandTimeout = null)
    {
        Dictionary<string, string?> settings = new(StringComparer.Ordinal)
        {
            ["Sql:main:ConnectionString"] = WebTestEnvironmentGate.SqlConnectionString
                ?? throw new InvalidOperationException($"Set {WebTestEnvironmentGate.SqlConnectionStringVariable} to run these tests."),
        };

        if (commandTimeout is not null)
            settings["Sql:main:CommandTimeout"] = commandTimeout;

        return ConfigInstance.Create()
            .OverrideConfig(settings)
            .LoadWebConfig()
            .AddWebSqlModels(models => models.For<AdoptedRow>().Table(TableName).Key(x => x.Id))
            .Build();
    }

    private static SqlScript CreateTableScript => SqlScript.FromText($"""
        IF OBJECT_ID('{TableName}', 'U') IS NULL
        CREATE TABLE [{TableName}] ([Id] INT NOT NULL PRIMARY KEY, [Name] NVARCHAR(200) NOT NULL);
        """);

    private static async Task<SqlExecutionFailedException> FailureOf(Timeline timeline, ConfigInstance config, string label)
    {
        TimelineRun run = await timeline.SetupRun(config).RunAsync();
        return Assert.IsType<SqlExecutionFailedException>(run.Step(label).LastResult.Exception);
    }

    [SqlServerFact]
    public async Task AStatementTheServerCannotParse_FailsTheStepNamingTheStatement()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Execute("main", "SELEC 1 FROM nowhere")).Name("broken")
            .Build();

        SqlExecutionFailedException failure = await FailureOf(timeline, CreateConfig(), "broken");

        Assert.Contains("SELEC 1 FROM nowhere", failure.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task AParameterTheStatementUsesButTheStepDoesNotBind_FailsTheStep()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Scalar<int>("main", "SELECT @missing")).Name("unbound")
            .Build();

        SqlExecutionFailedException failure = await FailureOf(timeline, CreateConfig(), "unbound");

        Assert.Contains("@missing", failure.ToString(), StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task AStatementOutlastingItsCommandTimeout_IsStoppedAtThatTimeout()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Execute("main", "WAITFOR DELAY '00:00:10'")).Name("slow")
                .WithTimeOut(TimeSpan.FromSeconds(30))
            .Build();

        Stopwatch stopwatch = Stopwatch.StartNew();
        await FailureOf(timeline, CreateConfig(commandTimeout: "00:00:01"), "slow");
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"The statement ran {stopwatch.Elapsed} under a one-second command timeout.");
    }

    [SqlServerFact]
    public async Task ARowTheApplicationWrote_IsAdoptedByKeyAndRemovedAtTeardown()
    {
        int id = Random.Shared.Next(100_000, 999_999);
        string key = id.ToString(CultureInfo.InvariantCulture);

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", CreateTableScript)).Name("schema")
            .Trigger(WebExt.Sql.Execute("main", $"INSERT INTO [{TableName}] (Id, Name) VALUES (@id, 'written elsewhere')")
                .WithParameter("id", Var.Const(id))).Name("application")
            .RegisterArtifact("adopted", WebExt.Artifact.Sql.Row<AdoptedRow>("main", Var.Const(key)))
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.SqlRow<AdoptedRow>("adopted").Select(row => row.Name).Should().Be("written elsewhere");
        Assert.Equal(0, await CountAsync(id));
    }

    [SqlServerFact]
    public async Task AdoptingARowThatIsNotThere_FailsWhenTheTestAsksForIt()
    {
        int id = Random.Shared.Next(100_000, 999_999);
        string key = id.ToString(CultureInfo.InvariantCulture);

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", CreateTableScript)).Name("schema")
            .RegisterArtifact("adopted", WebExt.Artifact.Sql.Row<AdoptedRow>("main", Var.Const(key)))
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig()).RunAsync();

        ArtifactDoesNotExistException missing = Assert.Throws<ArtifactDoesNotExistException>(() => run.SqlRow<AdoptedRow>("adopted"));
        Assert.Contains("'adopted'", missing.Message, StringComparison.Ordinal);
    }

    private static async Task<int> CountAsync(int id)
    {
        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Scalar<int>("main", $"SELECT COUNT(1) FROM [{TableName}] WHERE Id = @id")
                .WithParameter("id", Var.Const(id))).Name("count")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig()).RunAsync();
        run.EnsureRanToCompletion();
        return run.Step("count").LastResult.Result is SqlScalarResult<int> scalar ? scalar.Value : -1;
    }
}
