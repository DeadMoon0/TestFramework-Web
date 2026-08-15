using System.Collections.Generic;
using TestFramework.Web.Sql.Steps;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// Covers how a script is cut into batches, which decides what the server ever sees.
/// </summary>
/// <remarks>
/// <c>GO</c> is a client-side separator: whatever this splitter gets wrong is either sent to the
/// server as a syntax error or silently dropped.
/// </remarks>
public class SqlScriptTests
{
    [Fact]
    public void ASingleStatement_IsOneBatch()
        => Assert.Single(SqlScript.FromText("SELECT 1;").SplitBatches());

    [Fact]
    public void GO_SeparatesBatchesAndIsNotSentToTheServer()
    {
        IReadOnlyList<string> batches = SqlScript.FromText("""
            SELECT 1;
            GO
            SELECT 2;
            """).SplitBatches();

        Assert.Equal(["SELECT 1;", "SELECT 2;"], batches);
    }

    [Fact]
    public void GO_IsRecognizedRegardlessOfCaseAndIndentation()
    {
        IReadOnlyList<string> batches = SqlScript.FromText("""
            SELECT 1;
              go
            SELECT 2;
            """).SplitBatches();

        Assert.Equal(2, batches.Count);
    }

    [Fact]
    public void GO_WithARepeatCount_RepeatsTheBatch()
    {
        IReadOnlyList<string> batches = SqlScript.FromText("""
            INSERT INTO T (V) VALUES (1);
            GO 3
            SELECT COUNT(1) FROM T;
            """).SplitBatches();

        Assert.Equal(
            [
                "INSERT INTO T (V) VALUES (1);",
                "INSERT INTO T (V) VALUES (1);",
                "INSERT INTO T (V) VALUES (1);",
                "SELECT COUNT(1) FROM T;",
            ],
            batches);
    }

    [Fact]
    public void GO_WithATrailingComment_IsStillASeparator()
    {
        IReadOnlyList<string> batches = SqlScript.FromText("""
            SELECT 1;
            GO -- create the objects first
            SELECT 2;
            """).SplitBatches();

        Assert.Equal(["SELECT 1;", "SELECT 2;"], batches);
    }

    [Fact]
    public void AWordStartingWithGO_IsNotASeparator()
    {
        // GOTO and a column named GOAL must survive; the separator is a line of its own.
        IReadOnlyList<string> batches = SqlScript.FromText("SELECT Goal FROM Targets;").SplitBatches();

        Assert.Equal(["SELECT Goal FROM Targets;"], batches);
    }

    [Fact]
    public void TrailingAndRepeatedSeparators_ProduceNoEmptyBatches()
    {
        IReadOnlyList<string> batches = SqlScript.FromText("""
            SELECT 1;
            GO
            GO
            """).SplitBatches();

        Assert.Equal(["SELECT 1;"], batches);
    }
}
