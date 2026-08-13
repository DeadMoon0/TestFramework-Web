using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;

namespace TestFramework.Web.Sql.Model;

/// <summary>
/// Shared reflection rules for discovering mappable properties.
/// </summary>
public static class SqlModelReflection
{
    /// <summary>
    /// Returns the properties a model exposes as columns: public, readable, and of a scalar type.
    /// </summary>
    /// <param name="modelType">The model type to inspect.</param>
    public static IReadOnlyList<PropertyInfo> GetMappableProperties(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        return [.. modelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Where(property => IsScalar(property.PropertyType))
            .Where(property => property.GetCustomAttribute<NotMappedAttribute>() is null)];
    }

    private static bool IsScalar(Type type)
    {
        Type effective = Nullable.GetUnderlyingType(type) ?? type;

        // Navigation properties and collections are not columns. Byte arrays are, because they map
        // to varbinary.
        return effective.IsPrimitive
            || effective.IsEnum
            || effective == typeof(string)
            || effective == typeof(decimal)
            || effective == typeof(DateTime)
            || effective == typeof(DateTimeOffset)
            || effective == typeof(TimeSpan)
            || effective == typeof(Guid)
            || effective == typeof(byte[]);
    }
}

/// <summary>
/// Maps model types from explicit registrations.
/// </summary>
/// <param name="builder">The builder holding the registrations.</param>
public sealed class FluentSqlModelSource(SqlModelBuilder builder) : ISqlModelMapSource
{
    /// <inheritdoc />
    public bool TryResolve(Type modelType, out SqlModelMap? map)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        if (builder.Definitions.TryGetValue(modelType, out SqlModelDefinition? definition))
        {
            map = definition.Build();
            return true;
        }

        map = null;
        return false;
    }
}

/// <summary>
/// Maps model types from <see cref="System.ComponentModel.DataAnnotations"/> attributes.
/// </summary>
/// <remarks>
/// These attributes live in the base class library, so an annotated model needs no object-relational
/// mapper for the framework to understand it.
/// </remarks>
public sealed class DataAnnotationsSqlModelSource : ISqlModelMapSource
{
    /// <inheritdoc />
    public bool TryResolve(Type modelType, out SqlModelMap? map)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        IReadOnlyList<PropertyInfo> properties = SqlModelReflection.GetMappableProperties(modelType);
        PropertyInfo[] keys = [.. properties.Where(property => property.GetCustomAttribute<KeyAttribute>() is not null)];
        if (keys.Length == 0)
        {
            map = null;
            return false;
        }

        TableAttribute? table = modelType.GetCustomAttribute<TableAttribute>();
        List<SqlColumnMap> columns = [.. properties.Select(property => Describe(property, keys.Contains(property)))];

        List<SqlColumnMap> ordered = [
            .. columns.Where(column => column.IsKey),
            .. columns.Where(column => !column.IsKey),
        ];

        map = new SqlModelMap(modelType, table?.Schema, table?.Name ?? modelType.Name, ordered);
        return true;
    }

    private static SqlColumnMap Describe(PropertyInfo property, bool isKey)
    {
        DatabaseGeneratedAttribute? generated = property.GetCustomAttribute<DatabaseGeneratedAttribute>();

        return new SqlColumnMap(
            property,
            property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name,
            isKey,
            generated is { DatabaseGeneratedOption: DatabaseGeneratedOption.Identity or DatabaseGeneratedOption.Computed })
        {
            MaxLength = ResolveMaxLength(property),
            ColumnType = property.GetCustomAttribute<ColumnAttribute>()?.TypeName,
            IsIdentity = generated is { DatabaseGeneratedOption: DatabaseGeneratedOption.Identity },
            IsRequired = property.GetCustomAttribute<RequiredAttribute>() is not null,
        };
    }

    private static int? ResolveMaxLength(PropertyInfo property)
    {
        if (property.GetCustomAttribute<MaxLengthAttribute>() is { Length: > 0 } maxLength)
            return maxLength.Length;

        if (property.GetCustomAttribute<StringLengthAttribute>() is { MaximumLength: > 0 } stringLength)
            return stringLength.MaximumLength;

        return null;
    }
}

/// <summary>
/// Maps model types by convention: the type name is the table, and <c>Id</c> or <c>&lt;Type&gt;Id</c> is the key.
/// </summary>
public sealed class ConventionSqlModelSource : ISqlModelMapSource
{
    /// <inheritdoc />
    public bool TryResolve(Type modelType, out SqlModelMap? map)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        IReadOnlyList<PropertyInfo> properties = SqlModelReflection.GetMappableProperties(modelType);
        PropertyInfo? key = properties.FirstOrDefault(property => string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            ?? properties.FirstOrDefault(property => string.Equals(property.Name, modelType.Name + "Id", StringComparison.OrdinalIgnoreCase));

        if (key is null)
        {
            map = null;
            return false;
        }

        List<SqlColumnMap> columns = [
            new SqlColumnMap(key, key.Name, true, false),
            .. properties.Where(property => property != key).Select(property => new SqlColumnMap(property, property.Name, false, false)),
        ];

        map = new SqlModelMap(modelType, null, modelType.Name, columns);
        return true;
    }
}
