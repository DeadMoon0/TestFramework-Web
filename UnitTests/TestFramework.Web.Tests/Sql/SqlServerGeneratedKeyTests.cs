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
using TestFramework.Web.Tests.Shared;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// Rows whose key the database assigns, against a real SQL Server: each one is created, found again by the key
/// handed to the next step, and gone after teardown.
/// </summary>
/// <remarks>
/// "Gone after teardown" is the point. The leak this closes read as success - every run left one row behind and
/// logged that it had cleaned up - so each test counts what is left by a marker only that test wrote. See
/// <see cref="SqlServerRoundTripTests"/> for how to point these at a server.
/// </remarks>
[Trait("Category", "SqlServer")]
public class SqlServerGeneratedKeyTests
{
    public sealed class GenTicket
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public sealed class GenGuidRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    public sealed class GenLine
    {
        public int OrderId { get; set; }
        public int LineNo { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    public sealed class GenAudited
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    private static string ConnectionString =>
        WebTestEnvironmentGate.SqlConnectionString
        ?? throw new InvalidOperationException($"Set {WebTestEnvironmentGate.SqlConnectionStringVariable} to a SQL Server connection string to run these tests.");

    private static ConfigInstance CreateConfig()
        => ConfigInstance.Create()
            .OverrideConfig(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Sql:main:ConnectionString"] = ConnectionString,
            })
            .LoadWebConfig()
            .AddWebSqlModels(models =>
            {
                models.For<GenTicket>().Table("TFWebGenTicket").Key(x => x.Id).Identity(x => x.Id).Generated(x => x.CreatedAt);
                models.For<GenGuidRow>().Table("TFWebGenGuid").Key(x => x.Id).Generated(x => x.Id);
                models.For<GenLine>().Table("TFWebGenLine").Key(x => x.OrderId).Key(x => x.LineNo).Identity(x => x.LineNo);
                models.For<GenAudited>().Table("TFWebGenAudited").Key(x => x.Id).Identity(x => x.Id);
            })
            .Build();

    private static SqlScript Schema => SqlScript.FromText("""
        IF OBJECT_ID('TFWebGenTicket', 'U') IS NULL
        CREATE TABLE [TFWebGenTicket] ([Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [Title] NVARCHAR(200) NOT NULL, [CreatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
        IF OBJECT_ID('TFWebGenGuid', 'U') IS NULL
        CREATE TABLE [TFWebGenGuid] ([Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY, [Title] NVARCHAR(200) NOT NULL);
        IF OBJECT_ID('TFWebGenLine', 'U') IS NULL
        CREATE TABLE [TFWebGenLine] ([OrderId] INT NOT NULL, [LineNo] INT IDENTITY(1,1) NOT NULL, [Title] NVARCHAR(200) NOT NULL, PRIMARY KEY ([OrderId], [LineNo]));
        IF OBJECT_ID('TFWebGenAudited', 'U') IS NULL
        CREATE TABLE [TFWebGenAudited] ([Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY, [Title] NVARCHAR(200) NOT NULL);
        IF OBJECT_ID('TFWebGenAudit', 'U') IS NULL
        CREATE TABLE [TFWebGenAudit] ([AuditedId] INT NOT NULL, [Title] NVARCHAR(200) NOT NULL);
        GO
        CREATE OR ALTER TRIGGER [TFWebGenAuditedInsert] ON [TFWebGenAudited] AFTER INSERT AS
            INSERT INTO [TFWebGenAudit] ([AuditedId], [Title]) SELECT [Id], [Title] FROM inserted;
        """);

    [SqlServerFact]
    public async Task AnIdentityKey_IsReadBackHandedOnAndCleanedUp()
    {
        string marker = Marker();

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", Schema)).Label("schema")
            .SetupArtifact("ticket")
            .Trigger(WebExt.Sql.Scalar<int>("main", "SELECT COUNT(1) FROM [TFWebGenTicket] WHERE [Id] = @id AND [Title] = @title")
                .WithParameterFrom("id", Var.Ref<int>("ticketId"))
                .WithParameter("title", marker)).Label("found")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig())
            .AddArtifact(
                "ticket",
                WebExt.Artifact.Sql.Row<GenTicket>("main").KeyTo(x => x.Id, "ticketId"),
                new SqlRowArtifactData<GenTicket>(new GenTicket { Title = marker }))
            .RunAsync();

        run.EnsureRanToCompletion();
        run.SqlScalar<int>("found").Should().Be(1);

        // Read back after the insert, so the artifact holds what the database wrote, defaults included.
        run.SqlRow<GenTicket>("ticket").Select(ticket => ticket.CreatedAt != default).Should().Be(true);

        await AssertNothingLeftAsync("TFWebGenTicket", marker);
    }

    [SqlServerFact]
    public async Task AGuidDefaultKey_IsReadBackHandedOnAndCleanedUp()
    {
        string marker = Marker();

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", Schema)).Label("schema")
            .SetupArtifact("row")
            .Trigger(WebExt.Sql.Scalar<int>("main", "SELECT COUNT(1) FROM [TFWebGenGuid] WHERE [Id] = @id AND [Title] = @title")
                .WithParameterFrom("id", Var.Ref<Guid>("rowId"))
                .WithParameter("title", marker)).Label("found")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig())
            .AddArtifact(
                "row",
                WebExt.Artifact.Sql.Row<GenGuidRow>("main").KeyTo(x => x.Id, "rowId"),
                new SqlRowArtifactData<GenGuidRow>(new GenGuidRow { Title = marker }))
            .RunAsync();

        run.EnsureRanToCompletion();
        run.SqlScalar<int>("found").Should().Be(1);

        await AssertNothingLeftAsync("TFWebGenGuid", marker);
    }

    [SqlServerFact]
    public async Task ACompositeKey_KeepsTheSuppliedPartAndReadsTheAssignedPartBack()
    {
        string marker = Marker();
        int orderId = Random.Shared.Next(100_000, 999_999);

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", Schema)).Label("schema")
            .SetupArtifact("line")
            .Trigger(WebExt.Sql.Scalar<int>("main", "SELECT COUNT(1) FROM [TFWebGenLine] WHERE [OrderId] = @orderId AND [LineNo] = @lineNo AND [Title] = @title")
                .WithParameter("orderId", orderId)
                .WithParameterFrom("lineNo", Var.Ref<int>("lineNo"))
                .WithParameter("title", marker)).Label("found")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig())
            .AddArtifact(
                "line",
                WebExt.Artifact.Sql.Row<GenLine>("main", Var.Const(orderId.ToString(System.Globalization.CultureInfo.InvariantCulture))).KeyTo(x => x.LineNo, "lineNo"),
                new SqlRowArtifactData<GenLine>(new GenLine { OrderId = orderId, Title = marker }))
            .RunAsync();

        run.EnsureRanToCompletion();
        run.SqlScalar<int>("found").Should().Be(1);

        await AssertNothingLeftAsync("TFWebGenLine", marker);
    }

    [SqlServerFact]
    public async Task ATableWithATrigger_StillHandsBackItsKey()
    {
        // A bare OUTPUT clause is refused on a table with an enabled trigger, and audit triggers are common on the
        // tables a test seeds; the key comes back through a table variable instead.
        string marker = Marker();

        Timeline timeline = Timeline.Create()
            .Trigger(WebExt.Sql.Script("main", Schema)).Label("schema")
            .SetupArtifact("audited")
            .Trigger(WebExt.Sql.Scalar<int>("main", "SELECT COUNT(1) FROM [TFWebGenAudit] WHERE [AuditedId] = @id AND [Title] = @title")
                .WithParameterFrom("id", Var.Ref<int>("auditedId"))
                .WithParameter("title", marker)).Label("audited")
            .Trigger(WebExt.Sql.Execute("main", "DELETE FROM [TFWebGenAudit] WHERE [Title] = @title")
                .WithParameter("title", marker)).Label("forget-audit")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig())
            .AddArtifact(
                "audited",
                WebExt.Artifact.Sql.Row<GenAudited>("main").KeyTo(x => x.Id, "auditedId"),
                new SqlRowArtifactData<GenAudited>(new GenAudited { Title = marker }))
            .RunAsync();

        run.EnsureRanToCompletion();
        run.SqlScalar<int>("audited").Should().Be(1);

        await AssertNothingLeftAsync("TFWebGenAudited", marker);
    }

    private static string Marker() => $"generated-key-{Guid.NewGuid():N}";

    private static async Task AssertNothingLeftAsync(string table, string marker)
    {
        Timeline verify = Timeline.Create()
            .Trigger(WebExt.Sql.Scalar<int>("main", $"SELECT COUNT(1) FROM [{table}] WHERE [Title] = @title")
                .WithParameter("title", marker)).Label("left")
            .Build();

        TimelineRun run = await verify.SetupRun(CreateConfig()).RunAsync();

        run.EnsureRanToCompletion();
        run.SqlScalar<int>("left").Should().Be(0);
    }
}
