using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TestFramework.Web.Sql.Exceptions;
using TestFramework.Web.Sql.Model;
using TestFramework.Web.Sql.Schema;
using TestFramework.Web.Sql.Steps;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

public class SqlSchemaTests
{
    private sealed class ConventionRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int Quantity { get; set; }
        public decimal? Total { get; set; }
    }

    [Table("Orders", Schema = "sales")]
    private sealed class AnnotatedOrder
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("customer_name")]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "money")]
        public decimal Total { get; set; }

        [NotMapped]
        public string Ignored { get; set; } = string.Empty;
    }

    private sealed class CompositeKeyRow
    {
        public string Tenant { get; set; } = string.Empty;
        public int Number { get; set; }
        public string Payload { get; set; } = string.Empty;
    }

    private sealed class GuidKeyRow
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class TypedRow
    {
        public int Id { get; set; }
        public bool Flag { get; set; }
        public long Big { get; set; }
        public double Ratio { get; set; }
        public Guid Reference { get; set; }
        public DateTimeOffset Moment { get; set; }
        public TimeSpan Duration { get; set; }
        public byte[] Payload { get; set; } = [];
        public DayOfWeek Day { get; set; }
    }

    private sealed class UnsupportedRow
    {
        public int Id { get; set; }

        // A pointer counts as a primitive, so it maps to a column that has no SQL type.
        public nint Handle { get; set; }
    }

    private static SqlModelMap Map<TModel>(Action<SqlModelBuilder>? configure = null)
    {
        SqlModelBuilder builder = new();
        configure?.Invoke(builder);
        return SqlModelRegistry.CreateDefault(builder).Resolve<TModel>();
    }

    [Fact]
    public void CreateTable_DerivesColumnsNullabilityAndKeyFromTheConvention()
    {
        string sql = SqlSchema.CreateTable(Map<ConventionRow>());

        Assert.Contains("IF OBJECT_ID(N'[ConventionRow]', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [ConventionRow] (", sql, StringComparison.Ordinal);
        Assert.Contains("[Id] INT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Name] NVARCHAR(MAX) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Note] NVARCHAR(MAX) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Quantity] INT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Total] DECIMAL(18,6) NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT [PK_ConventionRow] PRIMARY KEY ([Id])", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTable_HonoursAnnotatedTableColumnLengthAndType()
    {
        string sql = SqlSchema.CreateTable(Map<AnnotatedOrder>());

        Assert.Contains("CREATE TABLE [sales].[Orders] (", sql, StringComparison.Ordinal);
        Assert.Contains("[Id] INT IDENTITY(1,1) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[customer_name] NVARCHAR(200) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Total] money NOT NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Ignored", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTable_HonoursFluentLengthPrecisionAndRequiredDeclarations()
    {
        SqlModelMap map = Map<ConventionRow>(models => models.For<ConventionRow>()
            .Key(x => x.Id)
            .Identity(x => x.Id)
            .MaxLength(x => x.Name, 120)
            .Required(x => x.Note)
            .Precision(x => x.Total, 12, 2));

        string sql = SqlSchema.CreateTable(map);

        Assert.Contains("[Id] INT IDENTITY(1,1) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Name] NVARCHAR(120) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Note] NVARCHAR(MAX) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Total] DECIMAL(12,2) NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTable_BoundsATextKeySoItCanBeIndexed()
    {
        SqlModelMap map = Map<CompositeKeyRow>(models => models.For<CompositeKeyRow>()
            .Key(x => x.Tenant)
            .Key(x => x.Number));

        string sql = SqlSchema.CreateTable(map);

        Assert.Contains("[Tenant] NVARCHAR(450) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT [PK_CompositeKeyRow] PRIMARY KEY ([Tenant], [Number])", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTable_GivesDatabaseAssignedGuidsAndTimestampsADefault()
    {
        SqlModelMap map = Map<GuidKeyRow>(models => models.For<GuidKeyRow>()
            .Key(x => x.Id)
            .Generated(x => x.Id)
            .Generated(x => x.CreatedAt));

        string sql = SqlSchema.CreateTable(map);

        Assert.Contains("[Id] UNIQUEIDENTIFIER CONSTRAINT [DF_GuidKeyRow_Id] DEFAULT NEWSEQUENTIALID() NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[CreatedAt] DATETIME2 CONSTRAINT [DF_GuidKeyRow_CreatedAt] DEFAULT SYSUTCDATETIME() NOT NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTable_MapsTheScalarTypesTheModelSurfaceSupports()
    {
        string sql = SqlSchema.CreateTable(Map<TypedRow>());

        Assert.Contains("[Flag] BIT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Big] BIGINT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Ratio] FLOAT NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Reference] UNIQUEIDENTIFIER NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Moment] DATETIMEOFFSET NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Duration] TIME NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Payload] VARBINARY(MAX) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[Day] INT NOT NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTable_FailsWhenAColumnTypeCannotBeDerived()
    {
        SqlSchemaGenerationException exception = Assert.Throws<SqlSchemaGenerationException>(() => SqlSchema.CreateTable(Map<UnsupportedRow>()));

        Assert.Contains("Handle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTable_FailsWhenANonIntegerColumnIsDeclaredAsIdentity()
    {
        SqlModelMap map = Map<GuidKeyRow>(models => models.For<GuidKeyRow>()
            .Key(x => x.Id)
            .Identity(x => x.Id));

        SqlSchemaGenerationException exception = Assert.Throws<SqlSchemaGenerationException>(() => SqlSchema.CreateTable(map));

        Assert.Contains("identities must be integers", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTables_CreatesEachSchemaOnceBeforeItsTables()
    {
        string sql = SqlSchema.CreateTables([Map<AnnotatedOrder>(), Map<ConventionRow>()]);

        int schemaIndex = sql.IndexOf("CREATE SCHEMA", StringComparison.Ordinal);
        int tableIndex = sql.IndexOf("CREATE TABLE [sales].[Orders]", StringComparison.Ordinal);

        Assert.Contains("IF SCHEMA_ID(N'sales') IS NULL EXEC(N'CREATE SCHEMA [sales];');", sql, StringComparison.Ordinal);
        Assert.True(schemaIndex >= 0 && schemaIndex < tableIndex);
        Assert.Contains("CREATE TABLE [ConventionRow]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTables_FailsWhenTwoModelsClaimTheSameTable()
    {
        SqlModelBuilder builder = new();
        builder.For<ConventionRow>().Table("Shared").Key(x => x.Id);
        builder.For<CompositeKeyRow>().Table("Shared").Key(x => x.Tenant);
        SqlModelRegistry registry = SqlModelRegistry.CreateDefault(builder);

        SqlSchemaGenerationException exception = Assert.Throws<SqlSchemaGenerationException>(
            () => SqlSchema.CreateTables([registry.Resolve<ConventionRow>(), registry.Resolve<CompositeKeyRow>()]));

        Assert.Contains("[Shared]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTablesScript_ProducesOneRunnableBatchNamedAfterItsModels()
    {
        SqlScript script = SqlSchema.CreateTablesScript(typeof(ConventionRow), typeof(AnnotatedOrder));

        Assert.Equal("schema for ConventionRow, AnnotatedOrder", script.Description);
        Assert.Single(script.SplitBatches());
    }
}
