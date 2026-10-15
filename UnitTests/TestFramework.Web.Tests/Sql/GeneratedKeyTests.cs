using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config;
using TestFramework.Core.Steps;
using TestFramework.Core.Timelines;
using TestFramework.Core.Timelines.Assertions;
using TestFramework.Core.Variables;
using TestFramework.Web.Extensions;
using TestFramework.Web.Sql.Artifacts;
using TestFramework.Web.Sql.Exceptions;
using TestFramework.Web.Sql.Execution;
using TestFramework.Web.Sql.Model;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// Rows whose key the database assigns: created by leaving the key out, addressed by the key read back, and
/// refused in every shape that would leave a row behind.
/// </summary>
/// <remarks>
/// The damage this guards against read as a clean run: a model declared <c>Generated(x =&gt; x.Id)</c> and seeded
/// with <c>Row&lt;T&gt;("main", "4711")</c> inserted a row the database numbered itself, teardown deleted 4711,
/// affected nothing and logged "already gone", and every run left one row in the database under test.
/// </remarks>
public class GeneratedKeyTests
{
    public sealed class Ticket
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    public sealed class Line
    {
        public int OrderId { get; set; }
        public int LineNo { get; set; }
        public string Sku { get; set; } = string.Empty;
    }

    public sealed class Order
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static ConfigInstance CreateConfig(RecordingSqlExecutor executor)
        => ConfigInstance.Create()
            .OverrideConfig(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Sql:main:Server"] = "localhost",
                ["Sql:main:Database"] = "SampleDb",
                ["Sql:main:IntegratedSecurity"] = "true",
            })
            .LoadWebConfig()
            .AddWebSqlModels(models =>
            {
                models.For<Ticket>().Table("Tickets").Key(x => x.Id).Identity(x => x.Id);
                models.For<Line>().Table("Lines").Key(x => x.OrderId).Key(x => x.LineNo).Identity(x => x.LineNo);
                models.For<Order>().Table("Orders").Key(x => x.Id);
            })
            .UseSqlExecutor(new FixedSqlExecutorFactory(executor))
            .Build();

    [Fact]
    public async Task ARowWhoseKeyTheDatabaseAssigns_IsCreatedAndAddressedByTheKeyItWasGiven()
    {
        RecordingSqlExecutor executor = new()
        {
            QueryResult = call => call.Contains("OUTPUT")
                ? new[] { new Ticket { Id = 42 } }
                : new[] { new Ticket { Id = 42, Title = "as stored" } },
        };

        Timeline timeline = Timeline.Create()
            .SetupArtifact("ticket")
            .AssertVariable(Var.Ref<int>("ticketId"), id => id == 42)
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor))
            .AddArtifact(
                "ticket",
                WebExt.Artifact.Sql.Row<Ticket>("main").KeyTo(x => x.Id, "ticketId"),
                new SqlRowArtifactData<Ticket>(new Ticket { Title = "seeded" }))
            .RunAsync();

        run.EnsureRanToCompletion();

        // Inserted without looking for a key first - there is none to look for.
        Assert.Empty(executor.CallsContaining("SELECT COUNT(1)"));
        Assert.Single(executor.CallsContaining("OUTPUT INSERTED.[Id] INTO @tf_out"));

        // Read back and captured, so the artifact shows the row the database holds.
        run.SqlRow<Ticket>("ticket").Select(ticket => ticket.Title).Should().Be("as stored");

        // And teardown deleted that row - 42, the key the database gave it.
        RecordedSqlCall delete = Assert.Single(executor.CallsContaining("DELETE FROM [Tickets]"));
        Assert.Equal(42, delete.Parameters["tf_key0"]);
    }

    [Fact]
    public async Task AKeyGivenForAColumnTheDatabaseAssigns_IsRefusedBeforeAnythingIsInserted()
    {
        RecordingSqlExecutor executor = new();

        Timeline timeline = Timeline.Create()
            .SetupArtifact("ticket").Label("setup")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor))
            .AddArtifact(
                "ticket",
                WebExt.Artifact.Sql.Row<Ticket>("main", Var.Const("4711")),
                new SqlRowArtifactData<Ticket>(new Ticket { Id = 4711, Title = "seeded" }))
            .RunAsync();

        SqlModelMapException refusal = Assert.IsType<SqlModelMapException>(run.Step("setup").LastResult.Exception);
        Assert.Contains("which the database assigns", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(refusal.RecoverySteps, step => step.Contains("Row<Ticket>(identifier)", StringComparison.Ordinal));
        Assert.Empty(executor.CallsContaining("INSERT"));
    }

    [Fact]
    public async Task ACompositeKey_TakesTheSuppliedPartAndReadsTheAssignedPartBack()
    {
        RecordingSqlExecutor executor = new()
        {
            QueryResult = call => call.Contains("OUTPUT")
                ? new[] { new Line { OrderId = 7, LineNo = 3 } }
                : new[] { new Line { OrderId = 7, LineNo = 3, Sku = "sku" } },
        };

        Timeline timeline = Timeline.Create()
            .SetupArtifact("line")
            .AssertVariable(Var.Ref<int>("lineNo"), lineNo => lineNo == 3)
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor))
            .AddArtifact(
                "line",
                WebExt.Artifact.Sql.Row<Line>("main", Var.Const("7")).KeyTo(x => x.LineNo, "lineNo"),
                new SqlRowArtifactData<Line>(new Line { OrderId = 7, Sku = "sku" }))
            .RunAsync();

        run.EnsureRanToCompletion();

        RecordedSqlCall delete = Assert.Single(executor.CallsContaining("DELETE FROM [Lines]"));
        Assert.Equal(7, delete.Parameters["tf_key0"]);
        Assert.Equal(3, delete.Parameters["tf_key1"]);
    }

    [Fact]
    public async Task KeyValuesThatAddressNoRow_AreRefusedNamingBothWaysOut()
    {
        RecordingSqlExecutor executor = new();

        Timeline timeline = Timeline.Create()
            .SetupArtifact("line").Label("setup")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor))
            .AddArtifact("line", WebExt.Artifact.Sql.Row<Line>("main"), new SqlRowArtifactData<Line>(new Line()))
            .RunAsync();

        SqlModelMapException refusal = Assert.IsType<SqlModelMapException>(run.Step("setup").LastResult.Exception);
        Assert.Contains("2 key column(s) (OrderId, LineNo) but 0 key value(s)", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(refusal.RecoverySteps, step => step.Contains("leave out LineNo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeedDataWhoseKeyDisagreesWithTheReference_IsRefused()
    {
        // Setup writes the key the row carries and teardown deletes the key the reference names, so a
        // disagreement is a row nobody deletes.
        RecordingSqlExecutor executor = new();

        Timeline timeline = Timeline.Create()
            .SetupArtifact("order").Label("setup")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor))
            .AddArtifact("order", WebExt.Artifact.Sql.Row<Order>("main", Var.Const("1")), new SqlRowArtifactData<Order>(new Order { Id = 2 }))
            .RunAsync();

        SqlModelMapException refusal = Assert.IsType<SqlModelMapException>(run.Step("setup").LastResult.Exception);
        Assert.Contains("carries Id = '2'", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(executor.CallsContaining("INSERT"));
    }

    [Fact]
    public async Task KeyToANonKeyProperty_IsRefused()
    {
        RecordingSqlExecutor executor = new();

        Timeline timeline = Timeline.Create()
            .SetupArtifact("ticket").Label("setup")
            .Build();

        TimelineRun run = await timeline.SetupRun(CreateConfig(executor))
            .AddArtifact(
                "ticket",
                WebExt.Artifact.Sql.Row<Ticket>("main").KeyTo(x => x.Title, "title"),
                new SqlRowArtifactData<Ticket>(new Ticket()))
            .RunAsync();

        SqlModelMapException refusal = Assert.IsType<SqlModelMapException>(run.Step("setup").LastResult.Exception);
        Assert.Contains("'Ticket.Title', which is not a key column", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyTo_RefusesTwoKeysIntoOneVariable()
    {
        SqlRowArtifactReference<Line> reference = WebExt.Artifact.Sql.Row<Line>("main", Var.Const("7")).KeyTo(x => x.LineNo, "key");

        Assert.Throws<ArgumentException>(() => reference.KeyTo(x => x.OrderId, "key"));
    }

    [Fact]
    public async Task ARowWhoseKeyWasNeverAssigned_IsNotDeleted()
    {
        RecordingSqlExecutor executor = new();
        using ServiceProvider services = CreateConfig(executor).BuildServiceProvider();
        RunContext context = RunContext.Detached(services);

        await new SqlRowArtifactDescriber<Ticket>().Deconstruct(context, WebExt.Artifact.Sql.Row<Ticket>("main"));

        Assert.Empty(executor.Calls);
    }

    [Fact]
    public void TheInsertReturnsTheKeyThroughATableVariable()
    {
        // OUTPUT ... INTO, because a bare OUTPUT is refused on a table with an enabled trigger.
        SqlModelBuilder models = new();
        models.For<Ticket>().Table("Tickets").Key(x => x.Id).Identity(x => x.Id);
        SqlModelMap map = SqlModelRegistry.CreateDefault(models).Resolve<Ticket>();

        string text = SqlStatementBuilder.InsertReturningKey(map).Text;

        Assert.Equal(
            "DECLARE @tf_out TABLE ([tf_out0] sql_variant); INSERT INTO [Tickets] ([Title]) OUTPUT INSERTED.[Id] INTO @tf_out VALUES (@tf_val0); SELECT [tf_out0] AS [Id] FROM @tf_out;",
            text);
    }

    public sealed class KeyKinds
    {
        public int Number { get; set; }
        public long Big { get; set; }
        public Guid Unique { get; set; }
        public DateTime Moment { get; set; }
        public DateTimeOffset Offset { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    [Fact]
    public void AKeyReadFromTheDatabase_AddressesTheSameRowAgain()
    {
        // A key is carried as text between the read-back and the delete. The general invariant format drops the
        // fraction of a second a datetime2 key keeps, which would aim teardown at a row that is not there.
        SqlModelBuilder models = new();
        models.For<KeyKinds>().Table("Keys")
            .Key(x => x.Number).Key(x => x.Big).Key(x => x.Unique).Key(x => x.Moment).Key(x => x.Offset).Key(x => x.Text);
        SqlModelMap map = SqlModelRegistry.CreateDefault(models).Resolve<KeyKinds>();

        KeyKinds row = new()
        {
            Number = -17,
            Big = long.MaxValue,
            Unique = Guid.NewGuid(),
            Moment = new DateTime(2026, 10, 2, 13, 14, 15, DateTimeKind.Unspecified).AddTicks(1234567),
            Offset = new DateTimeOffset(2026, 10, 2, 13, 14, 15, TimeSpan.FromHours(2)).AddTicks(7654321),
            Text = "a key, with text",
        };

        IReadOnlyList<string> text = map.ReadKeyValues(row);
        object[] readBack = [.. map.KeyColumns.Select((column, index) => map.ConvertKeyValue(column, text[index]))];

        Assert.Equal([row.Number, row.Big, row.Unique, row.Moment, row.Offset, row.Text], readBack);
    }
}
