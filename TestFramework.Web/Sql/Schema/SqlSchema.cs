using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TestFramework.Web.Sql.Exceptions;
using TestFramework.Web.Sql.Model;
using TestFramework.Web.Sql.Steps;

namespace TestFramework.Web.Sql.Schema;

/// <summary>
/// Generates table definitions from model maps.
/// </summary>
/// <remarks>
/// This exists so a fixture database can be created from the models a test already declares,
/// instead of from a hand-written script kept in step with them. It generates schemas, tables,
/// columns, nullability, identities and primary keys, and nothing else: no foreign keys, indexes,
/// check constraints or collations. It is scaffolding for a database the test owns, not a migration
/// tool. Where the real schema is owned elsewhere -- by migrations, or by whoever runs the server --
/// point the test at that schema instead, because a table generated from test-side models proves
/// only that the models agree with themselves.
/// </remarks>
public static class SqlSchema
{
    /// <summary>
    /// Generates the statement that creates one table when it does not exist yet.
    /// </summary>
    /// <param name="map">The model map describing the table.</param>
    /// <exception cref="SqlSchemaGenerationException">A column could not be described.</exception>
    public static string CreateTable(SqlModelMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        IEnumerable<string> definitions = map.Columns
            .Select(column => DescribeColumn(map, column))
            .Append(DescribePrimaryKey(map));

        StringBuilder builder = new();
        builder.AppendLine($"IF OBJECT_ID(N'{QuoteLiteral(map.QualifiedTable)}', N'U') IS NULL");
        builder.AppendLine($"CREATE TABLE {map.QualifiedTable} (");
        builder.AppendLine(string.Join($",{Environment.NewLine}", definitions.Select(definition => $"    {definition}")));
        builder.Append(");");

        return builder.ToString();
    }

    /// <summary>
    /// Generates the statement that creates a schema when it does not exist yet.
    /// </summary>
    /// <param name="schema">The schema name.</param>
    /// <remarks>
    /// <c>CREATE SCHEMA</c> must be the first statement of its batch, so it is wrapped in
    /// <c>EXEC</c> to keep the whole script runnable as one batch.
    /// </remarks>
    public static string CreateSchema(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        return $"IF SCHEMA_ID(N'{QuoteLiteral(schema)}') IS NULL EXEC(N'CREATE SCHEMA {QuoteLiteral(SqlModelMap.Quote(schema))};');";
    }

    /// <summary>
    /// Generates the schemas and tables for several models, in the order they are given.
    /// </summary>
    /// <param name="maps">The model maps to generate.</param>
    /// <exception cref="SqlSchemaGenerationException">Two models map to the same table, or a column could not be described.</exception>
    public static string CreateTables(IEnumerable<SqlModelMap> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);

        SqlModelMap[] ordered = [.. maps];
        EnsureDistinctTables(ordered);

        IEnumerable<string> schemas = ordered
            .Select(map => map.Schema)
            .Where(schema => !string.IsNullOrWhiteSpace(schema) && !string.Equals(schema, "dbo", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Select(schema => CreateSchema(schema!));

        return string.Join(
            $"{Environment.NewLine}{Environment.NewLine}",
            schemas.Concat(ordered.Select(CreateTable)));
    }

    /// <summary>
    /// Generates a runnable script that creates the tables of several models.
    /// </summary>
    /// <param name="maps">The model maps to generate.</param>
    public static SqlScript CreateTablesScript(IEnumerable<SqlModelMap> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);

        SqlModelMap[] ordered = [.. maps];
        return SqlScript.FromText(
            CreateTables(ordered),
            $"schema for {string.Join(", ", ordered.Select(map => map.ModelType.Name))}");
    }

    /// <summary>
    /// Generates a runnable script that creates the tables of several model types.
    /// </summary>
    /// <param name="registry">The registry resolving each model type to its map.</param>
    /// <param name="modelTypes">The model types to generate.</param>
    public static SqlScript CreateTablesScript(SqlModelRegistry registry, IEnumerable<Type> modelTypes)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(modelTypes);

        return CreateTablesScript(modelTypes.Select(registry.Resolve));
    }

    /// <summary>
    /// Generates a runnable script that creates the tables of several model types, mapping them by
    /// attributes and convention.
    /// </summary>
    /// <param name="modelTypes">The model types to generate.</param>
    public static SqlScript CreateTablesScript(params Type[] modelTypes)
        => CreateTablesScript(SqlModelRegistry.CreateDefault(), modelTypes);

    private static void EnsureDistinctTables(IReadOnlyList<SqlModelMap> maps)
    {
        Dictionary<string, Type> tables = [];
        foreach (SqlModelMap map in maps)
        {
            if (tables.TryGetValue(map.QualifiedTable, out Type? existing))
                throw SqlSchemaGenerationException.DuplicateTable(map.QualifiedTable, existing, map.ModelType);

            tables[map.QualifiedTable] = map.ModelType;
        }
    }

    private static string DescribeColumn(SqlModelMap map, SqlColumnMap column)
    {
        string definition = $"{SqlModelMap.Quote(column.ColumnName)} {SqlColumnTypeResolver.ResolveType(map, column)}";

        if (column.IsGenerated)
            definition = ApplyGeneratedValue(map, column, definition);

        return $"{definition} {(SqlColumnTypeResolver.IsNullable(column) ? "NULL" : "NOT NULL")}";
    }

    private static string ApplyGeneratedValue(SqlModelMap map, SqlColumnMap column, string definition)
    {
        Type clrType = column.ClrType.IsEnum ? Enum.GetUnderlyingType(column.ClrType) : column.ClrType;

        if (IsIntegral(clrType))
            return $"{definition} IDENTITY(1,1)";

        // An identity that is not an integer is a mistake worth naming, rather than quietly turning
        // into a default.
        if (column.IsIdentity)
            throw SqlSchemaGenerationException.NonIntegerIdentity(map.ModelType, column.ColumnName, column.ClrType);

        if (clrType == typeof(Guid))
            return $"{definition} {DescribeDefault(map, column, "NEWSEQUENTIALID()")}";

        if (clrType == typeof(DateTime) || clrType == typeof(DateTimeOffset))
            return $"{definition} {DescribeDefault(map, column, "SYSUTCDATETIME()")}";

        throw SqlSchemaGenerationException.UndeterminableGeneratedValue(map.ModelType, column.ColumnName, column.ClrType);
    }

    private static string DescribeDefault(SqlModelMap map, SqlColumnMap column, string expression)
        => $"CONSTRAINT {SqlModelMap.Quote($"DF_{map.Table}_{column.ColumnName}")} DEFAULT {expression}";

    private static string DescribePrimaryKey(SqlModelMap map)
    {
        string columns = string.Join(", ", map.KeyColumns.Select(column => SqlModelMap.Quote(column.ColumnName)));
        return $"CONSTRAINT {SqlModelMap.Quote($"PK_{map.Table}")} PRIMARY KEY ({columns})";
    }

    private static bool IsIntegral(Type clrType)
        => clrType == typeof(byte)
        || clrType == typeof(sbyte)
        || clrType == typeof(short)
        || clrType == typeof(ushort)
        || clrType == typeof(int)
        || clrType == typeof(uint)
        || clrType == typeof(long);

    private static string QuoteLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
