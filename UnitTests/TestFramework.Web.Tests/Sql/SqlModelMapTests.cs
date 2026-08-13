using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using TestFramework.Web.Sql.Exceptions;
using TestFramework.Web.Sql.Model;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

public class SqlModelMapTests
{
    private sealed class ConventionOrder
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    private sealed class SuffixKeyOrder
    {
        public int SuffixKeyOrderId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    [Table("Orders", Schema = "sales")]
    private sealed class AnnotatedOrder
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("customer_name")]
        public string Name { get; set; } = string.Empty;

        [NotMapped]
        public string Ignored { get; set; } = string.Empty;
    }

    private sealed class CompositeKeyRow
    {
        public string Tenant { get; set; } = string.Empty;
        public int Number { get; set; }
        public string Payload { get; set; } = string.Empty;
    }

    private sealed class NoKeyRow
    {
        public string Anything { get; set; } = string.Empty;
    }

    [Fact]
    public void Convention_UsesTypeNameAsTableAndIdAsKey()
    {
        SqlModelMap map = SqlModelRegistry.CreateDefault().Resolve<ConventionOrder>();

        Assert.Equal("ConventionOrder", map.Table);
        Assert.Null(map.Schema);
        Assert.Equal("[ConventionOrder]", map.QualifiedTable);
        Assert.Equal(["Id"], map.KeyColumns.Select(column => column.ColumnName));
        Assert.Equal(3, map.Columns.Count);
    }

    [Fact]
    public void Convention_AlsoAcceptsTypeNameIdAsKey()
    {
        SqlModelMap map = SqlModelRegistry.CreateDefault().Resolve<SuffixKeyOrder>();

        Assert.Equal(["SuffixKeyOrderId"], map.KeyColumns.Select(column => column.ColumnName));
    }

    [Fact]
    public void DataAnnotations_ReadTableSchemaColumnKeyAndGenerated()
    {
        SqlModelMap map = SqlModelRegistry.CreateDefault().Resolve<AnnotatedOrder>();

        Assert.Equal("sales", map.Schema);
        Assert.Equal("Orders", map.Table);
        Assert.Equal("[sales].[Orders]", map.QualifiedTable);
        Assert.Equal(["Id"], map.KeyColumns.Select(column => column.ColumnName));
        Assert.Equal("customer_name", map.FindByProperty(nameof(AnnotatedOrder.Name))!.ColumnName);

        // Identity columns are never written, and [NotMapped] is not a column at all.
        Assert.DoesNotContain(map.WritableColumns, column => column.ColumnName == "Id");
        Assert.Null(map.FindByProperty(nameof(AnnotatedOrder.Ignored)));
    }

    [Fact]
    public void FluentRegistration_WinsOverAttributesAndConvention()
    {
        SqlModelBuilder builder = new();
        builder.For<AnnotatedOrder>().Schema("dbo").Table("OverriddenOrders").Key(x => x.Name);

        SqlModelMap map = SqlModelRegistry.CreateDefault(builder).Resolve<AnnotatedOrder>();

        Assert.Equal("[dbo].[OverriddenOrders]", map.QualifiedTable);
        Assert.Equal(["Name"], map.KeyColumns.Select(column => column.ColumnName));
    }

    [Fact]
    public void FluentRegistration_SupportsCompositeKeysInDeclarationOrder()
    {
        SqlModelBuilder builder = new();
        builder.For<CompositeKeyRow>().Key(x => x.Tenant).Key(x => x.Number);

        SqlModelMap map = SqlModelRegistry.CreateDefault(builder).Resolve<CompositeKeyRow>();

        Assert.Equal(["Tenant", "Number"], map.KeyColumns.Select(column => column.ColumnName));
    }

    [Fact]
    public void FluentRegistration_CanRenameAndIgnoreColumns()
    {
        SqlModelBuilder builder = new();
        builder.For<ConventionOrder>().Key(x => x.Id).Column(x => x.Name, "order_name").Ignore(x => x.Quantity);

        SqlModelMap map = SqlModelRegistry.CreateDefault(builder).Resolve<ConventionOrder>();

        Assert.Equal("order_name", map.FindByProperty(nameof(ConventionOrder.Name))!.ColumnName);
        Assert.Null(map.FindByProperty(nameof(ConventionOrder.Quantity)));
    }

    [Fact]
    public void UnmappableType_FailsWithGuidanceForAllThreeSources()
    {
        SqlModelMapException exception = Assert.Throws<SqlModelMapException>(() => SqlModelRegistry.CreateDefault().Resolve<NoKeyRow>());

        Assert.Contains("AddWebSqlModels", exception.Message, StringComparison.Ordinal);
        Assert.Contains("[Key]", exception.Message, StringComparison.Ordinal);
        Assert.Contains("NoKeyRowId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyValues_AreConvertedToTheMappedColumnType()
    {
        SqlModelMap map = SqlModelRegistry.CreateDefault().Resolve<ConventionOrder>();

        object converted = map.ConvertKeyValue(map.KeyColumns[0], "42");

        Assert.Equal(42, Assert.IsType<int>(converted));
    }

    [Fact]
    public void KeyValues_FailWithTheColumnAndTargetTypeNamed_WhenTheyDoNotParse()
    {
        SqlModelMap map = SqlModelRegistry.CreateDefault().Resolve<ConventionOrder>();

        SqlModelMapException exception = Assert.Throws<SqlModelMapException>(() => map.ConvertKeyValue(map.KeyColumns[0], "not-a-number"));

        Assert.Contains("not-a-number", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Int32", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Orders", "[Orders]")]
    [InlineData("Order Details", "[Order Details]")]
    [InlineData("Weird]Name", "[Weird]]Name]")]
    public void Identifiers_AreBracketQuotedAndEscaped(string identifier, string expected)
        => Assert.Equal(expected, SqlModelMap.Quote(identifier));
}
