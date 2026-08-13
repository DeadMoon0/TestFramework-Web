using System;
using System.Globalization;
using System.Reflection;
using TestFramework.Web.Sql.Exceptions;
using TestFramework.Web.Sql.Model;

namespace TestFramework.Web.Sql.Schema;

/// <summary>
/// Derives the SQL Server type and nullability of a column from its mapped property.
/// </summary>
/// <remarks>
/// A CLR type carries less information than a column does: it has no length, precision or
/// collation. The defaults here are chosen so a generated table round-trips the model without
/// silently truncating, and every one of them can be overridden on the model map.
/// </remarks>
public static class SqlColumnTypeResolver
{
    /// <summary>
    /// Length used for a text key column, which cannot be <c>MAX</c> because a key must be indexable.
    /// </summary>
    public const int DefaultKeyLength = 450;

    /// <summary>
    /// Precision used for a decimal column with no declared precision.
    /// </summary>
    public const int DefaultDecimalPrecision = 18;

    /// <summary>
    /// Scale used for a decimal column with no declared scale.
    /// </summary>
    /// <remarks>
    /// SQL Server itself defaults to a scale of zero, which turns every stored fraction into a
    /// silent rounding error. A wide default is used instead, so a round-trip test compares equal.
    /// </remarks>
    public const int DefaultDecimalScale = 6;

    /// <summary>
    /// Returns the SQL type of a column, such as <c>NVARCHAR(200)</c>.
    /// </summary>
    /// <param name="map">The model map the column belongs to.</param>
    /// <param name="column">The column to type.</param>
    /// <exception cref="SqlSchemaGenerationException">The CLR type has no SQL Server equivalent.</exception>
    public static string ResolveType(SqlModelMap map, SqlColumnMap column)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(column);

        if (!string.IsNullOrWhiteSpace(column.ColumnType))
            return column.ColumnType;

        Type clrType = column.ClrType.IsEnum ? Enum.GetUnderlyingType(column.ClrType) : column.ClrType;

        if (clrType == typeof(string))
            return $"NVARCHAR({ResolveLength(column)})";

        if (clrType == typeof(byte[]))
            return $"VARBINARY({ResolveLength(column)})";

        if (clrType == typeof(decimal))
            return $"DECIMAL({FormatInt(column.Precision ?? DefaultDecimalPrecision)},{FormatInt(column.Scale ?? DefaultDecimalScale)})";

        return ResolveSimpleType(clrType) ?? throw SqlSchemaGenerationException.UnsupportedType(map.ModelType, column.ColumnName, column.ClrType);
    }

    /// <summary>
    /// Returns whether a column accepts nulls.
    /// </summary>
    /// <param name="column">The column to inspect.</param>
    /// <remarks>
    /// Key columns and columns declared required never do. Otherwise the property decides: a value
    /// type is not nullable, and a reference type follows its nullable annotation.
    /// </remarks>
    public static bool IsNullable(SqlColumnMap column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (column.IsKey || column.IsRequired)
            return false;

        Type propertyType = column.Property.PropertyType;
        if (Nullable.GetUnderlyingType(propertyType) is not null)
            return true;

        if (propertyType.IsValueType)
            return false;

        // A model compiled without nullable annotations reports Unknown; treating that as nullable
        // keeps generation from rejecting rows the model would happily carry.
        NullabilityInfo nullability = new NullabilityInfoContext().Create(column.Property);
        return nullability.ReadState != NullabilityState.NotNull;
    }

    private static string ResolveLength(SqlColumnMap column)
    {
        if (column.MaxLength is { } declared)
            return FormatInt(declared);

        return column.IsKey ? FormatInt(DefaultKeyLength) : "MAX";
    }

    private static string? ResolveSimpleType(Type clrType)
    {
        if (clrType == typeof(bool))
            return "BIT";
        if (clrType == typeof(byte))
            return "TINYINT";
        if (clrType == typeof(sbyte) || clrType == typeof(short))
            return "SMALLINT";
        if (clrType == typeof(ushort) || clrType == typeof(int))
            return "INT";
        if (clrType == typeof(uint) || clrType == typeof(long))
            return "BIGINT";
        if (clrType == typeof(ulong))
            return "DECIMAL(20,0)";
        if (clrType == typeof(float))
            return "REAL";
        if (clrType == typeof(double))
            return "FLOAT";
        if (clrType == typeof(char))
            return "NCHAR(1)";
        if (clrType == typeof(Guid))
            return "UNIQUEIDENTIFIER";
        if (clrType == typeof(DateTime))
            return "DATETIME2";
        if (clrType == typeof(DateTimeOffset))
            return "DATETIMEOFFSET";
        if (clrType == typeof(TimeSpan))
            return "TIME";

        return null;
    }

    private static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);
}
