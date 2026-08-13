using System;
using System.Linq;
using TestFramework.Web.Sql.Execution;
using TestFramework.Web.Sql.Model;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

public class SqlStatementBuilderTests
{
    private sealed class Order
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    private sealed class CompositeRow
    {
        public string Tenant { get; set; } = string.Empty;
        public int Number { get; set; }
        public string Payload { get; set; } = string.Empty;
    }

    private sealed class KeyOnlyRow
    {
        public int Id { get; set; }
    }

    private static SqlModelMap Map<TRow>(Action<SqlModelBuilder>? configure = null)
    {
        SqlModelBuilder builder = new();
        configure?.Invoke(builder);
        return SqlModelRegistry.CreateDefault(builder).Resolve<TRow>();
    }

    [Fact]
    public void SelectByKey_ProjectsEveryColumnAndFiltersOnTheKey()
    {
        SqlStatement statement = SqlStatementBuilder.SelectByKey(Map<Order>());

        Assert.Equal("SELECT [Id], [Name], [Quantity] FROM [Order] WHERE [Id] = @tf_key0;", statement.Text);
        Assert.Equal(["tf_key0"], statement.ParameterNames);
    }

    [Fact]
    public void SelectByKey_AliasesColumnsWhoseNameDiffersFromTheProperty()
    {
        SqlModelMap map = Map<Order>(builder => builder.For<Order>().Key(x => x.Id).Column(x => x.Name, "order_name"));

        SqlStatement statement = SqlStatementBuilder.SelectByKey(map);

        // Without the alias the row would not materialize onto the property.
        Assert.Contains("[order_name] AS [Name]", statement.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeKeys_ProduceOneParameterPerKeyColumnInOrder()
    {
        SqlModelMap map = Map<CompositeRow>(builder => builder.For<CompositeRow>().Key(x => x.Tenant).Key(x => x.Number));

        SqlStatement statement = SqlStatementBuilder.DeleteByKey(map);

        Assert.Equal("DELETE FROM [CompositeRow] WHERE [Tenant] = @tf_key0 AND [Number] = @tf_key1;", statement.Text);
        Assert.Equal(["tf_key0", "tf_key1"], statement.ParameterNames);
    }

    [Fact]
    public void Insert_SkipsGeneratedColumns()
    {
        SqlModelMap map = Map<Order>(builder => builder.For<Order>().Key(x => x.Id).Generated(x => x.Id));

        SqlStatement statement = SqlStatementBuilder.Insert(map);

        Assert.Equal("INSERT INTO [Order] ([Name], [Quantity]) VALUES (@tf_val0, @tf_val1);", statement.Text);
    }

    [Fact]
    public void Insert_WritesTheKey_WhenItIsNotGenerated()
    {
        SqlStatement statement = SqlStatementBuilder.Insert(Map<Order>());

        Assert.Equal("INSERT INTO [Order] ([Id], [Name], [Quantity]) VALUES (@tf_val0, @tf_val1, @tf_val2);", statement.Text);
    }

    [Fact]
    public void UpdateByKey_AssignsNonKeyColumnsAndFiltersOnTheKey()
    {
        SqlStatement statement = SqlStatementBuilder.UpdateByKey(Map<Order>());

        Assert.Equal("UPDATE [Order] SET [Name] = @tf_val0, [Quantity] = @tf_val1 WHERE [Id] = @tf_key0;", statement.Text);
        Assert.Equal(["tf_val0", "tf_val1", "tf_key0"], statement.ParameterNames);
    }

    [Fact]
    public void UpdateByKey_IsEmpty_WhenTheTableIsNothingButItsKey()
    {
        // Nothing to assign means nothing to update; setup falls back to insert-or-nothing.
        SqlStatement statement = SqlStatementBuilder.UpdateByKey(Map<KeyOnlyRow>());

        Assert.Empty(statement.Text);
    }

    [Fact]
    public void ExistsByKey_CountsRatherThanSelectingTheRow()
    {
        SqlStatement statement = SqlStatementBuilder.ExistsByKey(Map<Order>());

        Assert.Equal("SELECT COUNT(1) FROM [Order] WHERE [Id] = @tf_key0;", statement.Text);
    }

    [Fact]
    public void SelectWhere_KeepsTheCallerPredicateAndParameterNames()
    {
        SqlStatement statement = SqlStatementBuilder.SelectWhere(Map<Order>(), "Name = @name AND Quantity > @min", ["name", "min"]);

        Assert.Equal("SELECT [Id], [Name], [Quantity] FROM [Order] WHERE Name = @name AND Quantity > @min;", statement.Text);
        Assert.Equal(["name", "min"], statement.ParameterNames);
    }

    [Fact]
    public void SchemaQualifiedTables_AreQuotedInEveryStatement()
    {
        SqlModelMap map = Map<Order>(builder => builder.For<Order>().Schema("sales").Table("Orders").Key(x => x.Id));

        Assert.All(
            new[]
            {
                SqlStatementBuilder.SelectByKey(map).Text,
                SqlStatementBuilder.Insert(map).Text,
                SqlStatementBuilder.UpdateByKey(map).Text,
                SqlStatementBuilder.DeleteByKey(map).Text,
                SqlStatementBuilder.ExistsByKey(map).Text,
            },
            text => Assert.Contains("[sales].[Orders]", text, StringComparison.Ordinal));
    }
}
