using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TestFramework.Web.Sql.Exceptions;

namespace TestFramework.Web.Sql.Model;

/// <summary>
/// Describes how one property maps to one column.
/// </summary>
public sealed record SqlColumnMap
{
    internal SqlColumnMap(PropertyInfo property, string columnName, bool isKey, bool isGenerated)
    {
        this.Property = property;
        this.ColumnName = columnName;
        this.IsKey = isKey;
        this.IsGenerated = isGenerated;
    }

    /// <summary>
    /// The model property.
    /// </summary>
    public PropertyInfo Property { get; }

    /// <summary>
    /// The column name.
    /// </summary>
    public string ColumnName { get; }

    /// <summary>
    /// Whether the column participates in the primary key.
    /// </summary>
    public bool IsKey { get; }

    /// <summary>
    /// Whether the database assigns the value, so it is never written.
    /// </summary>
    public bool IsGenerated { get; }

    /// <summary>
    /// The CLR type of the mapped property, with nullability removed.
    /// </summary>
    public Type ClrType => Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType;

    /// <summary>
    /// The declared maximum length of a text or binary column.
    /// </summary>
    /// <remarks>
    /// Only schema generation reads this. Reading and writing rows works without it.
    /// </remarks>
    public int? MaxLength { get; init; }

    /// <summary>
    /// The declared precision of a numeric column.
    /// </summary>
    public int? Precision { get; init; }

    /// <summary>
    /// The declared scale of a numeric column.
    /// </summary>
    public int? Scale { get; init; }

    /// <summary>
    /// An explicit SQL column type that replaces the inferred one.
    /// </summary>
    /// <remarks>
    /// The escape hatch for a type the generator cannot infer, such as <c>money</c> or <c>xml</c>.
    /// It is emitted verbatim.
    /// </remarks>
    public string? ColumnType { get; init; }

    /// <summary>
    /// Whether the database assigns the value from an identity sequence.
    /// </summary>
    public bool IsIdentity { get; init; }

    /// <summary>
    /// Whether the column was explicitly declared as not accepting nulls.
    /// </summary>
    /// <remarks>
    /// When this is <see langword="false"/>, schema generation infers nullability from the property
    /// type and its nullable annotations instead.
    /// </remarks>
    public bool IsRequired { get; init; }
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
        GeneratedKeyColumns = [.. KeyColumns.Where(column => column.IsGenerated)];
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
    /// The key columns the database assigns, in key order: a row the test creates leaves them out and reads them
    /// back.
    /// </summary>
    internal IReadOnlyList<SqlColumnMap> GeneratedKeyColumns { get; }

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

            if (column.ClrType == typeof(DateTimeOffset))
                return DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);

            if (column.ClrType == typeof(DateTime))
                return DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);

            return Convert.ChangeType(value, column.ClrType, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw SqlModelMapException.KeyConversionFailed(ModelType, column.ColumnName, value, column.ClrType, exception);
        }
    }

    /// <summary>
    /// Reads a row's key values as the text a reference addresses it by, in key order.
    /// </summary>
    /// <param name="row">A row of this map's model.</param>
    /// <returns>The key values.</returns>
    /// <exception cref="TestFramework.Core.Exceptions.FrameworkStateException">A key column is null, so the row cannot be addressed.</exception>
    internal IReadOnlyList<string> ReadKeyValues(object row)
        => [.. KeyColumns.Select(column => column.Property.GetValue(row) is { } value
            ? FormatKeyValue(value)
            : throw new TestFramework.Core.Exceptions.FrameworkStateException(
                $"The key column '{column.ColumnName}' of '{ModelType.Name}' resolved to null, so the row cannot be addressed."))];

    /// <summary>
    /// Writes a key value as the text <see cref="ConvertKeyValue"/> reads back to the same value.
    /// </summary>
    /// <remarks>
    /// Dates round-trip format, so a key read from the database addresses the same row again: the general
    /// invariant format drops the fraction of a second a <c>datetime2</c> key keeps.
    /// </remarks>
    /// <param name="value">The key value.</param>
    /// <returns>The text.</returns>
    internal static string FormatKeyValue(object value) => value switch
    {
        string text => text,
        DateTime moment => moment.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset moment => moment.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Returns a readable description of the mapping.
    /// </summary>
    public override string ToString() => $"{ModelType.Name} -> {QualifiedTable}";
}
