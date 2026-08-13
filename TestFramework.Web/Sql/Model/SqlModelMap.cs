using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TestFramework.Web.Sql.Exceptions;

namespace TestFramework.Web.Sql.Model;

/// <summary>
/// Describes how one property maps to one column.
/// </summary>
/// <param name="Property">The model property.</param>
/// <param name="ColumnName">The column name.</param>
/// <param name="IsKey">Whether the column participates in the primary key.</param>
/// <param name="IsGenerated">Whether the database assigns the value, so it is never written.</param>
public sealed record SqlColumnMap(PropertyInfo Property, string ColumnName, bool IsKey, bool IsGenerated)
{
    /// <summary>
    /// The CLR type of the mapped property, with nullability removed.
    /// </summary>
    public Type ClrType { get; } = Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType;
}

/// <summary>
/// Describes how a model type maps to a table.
/// </summary>
/// <remarks>
/// This is the abstraction the whole SQL surface rests on. The framework reaches a database through
/// a connection string and a map; which data access library the application itself uses is
/// irrelevant.
/// </remarks>
public sealed class SqlModelMap
{
    internal SqlModelMap(Type modelType, string? schema, string table, IReadOnlyList<SqlColumnMap> columns)
    {
        if (columns.Count == 0)
            throw SqlModelMapException.NoColumns(modelType);

        if (!columns.Any(column => column.IsKey))
            throw SqlModelMapException.NoKey(modelType);

        ModelType = modelType;
        Schema = schema;
        Table = table;
        Columns = columns;
        KeyColumns = [.. columns.Where(column => column.IsKey)];
        WritableColumns = [.. columns.Where(column => !column.IsGenerated)];
    }

    /// <summary>
    /// The mapped model type.
    /// </summary>
    public Type ModelType { get; }

    /// <summary>
    /// The schema, when one was configured.
    /// </summary>
    public string? Schema { get; }

    /// <summary>
    /// The table name.
    /// </summary>
    public string Table { get; }

    /// <summary>
    /// Every mapped column.
    /// </summary>
    public IReadOnlyList<SqlColumnMap> Columns { get; }

    /// <summary>
    /// The columns forming the primary key, in declaration order.
    /// </summary>
    public IReadOnlyList<SqlColumnMap> KeyColumns { get; }

    /// <summary>
    /// The columns the framework writes, excluding database-generated ones.
    /// </summary>
    public IReadOnlyList<SqlColumnMap> WritableColumns { get; }

    /// <summary>
    /// The bracket-quoted, schema-qualified table name.
    /// </summary>
    /// <remarks>
    /// Identifiers are quoted rather than parameterized because SQL Server does not accept
    /// parameters in that position. They come from the map, never from test input.
    /// </remarks>
    public string QualifiedTable => Schema is null ? Quote(Table) : $"{Quote(Schema)}.{Quote(Table)}";

    /// <summary>
    /// Bracket-quotes an identifier, escaping any closing bracket.
    /// </summary>
    /// <param name="identifier">The identifier to quote.</param>
    public static string Quote(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    /// <summary>
    /// Returns the column mapped to a property name, or <see langword="null"/> when it is unmapped.
    /// </summary>
    /// <param name="propertyName">The property name.</param>
    public SqlColumnMap? FindByProperty(string propertyName)
        => Columns.FirstOrDefault(column => string.Equals(column.Property.Name, propertyName, StringComparison.Ordinal));

    /// <summary>
    /// Converts a key value supplied as text to the CLR type of its key column.
    /// </summary>
    /// <param name="column">The key column.</param>
    /// <param name="value">The value as text.</param>
    /// <returns>The converted value.</returns>
    public object ConvertKeyValue(SqlColumnMap column, string value)
    {
        ArgumentNullException.ThrowIfNull(column);

        try
        {
            if (column.ClrType == typeof(string))
                return value;

            if (column.ClrType == typeof(Guid))
                return Guid.Parse(value);

            return Convert.ChangeType(value, column.ClrType, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw SqlModelMapException.KeyConversionFailed(ModelType, column.ColumnName, value, column.ClrType, exception);
        }
    }

    /// <summary>
    /// Returns a readable description of the mapping.
    /// </summary>
    public override string ToString() => $"{ModelType.Name} -> {QualifiedTable}";
}
