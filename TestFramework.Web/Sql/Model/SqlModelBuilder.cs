using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using TestFramework.Web.Sql.Exceptions;

namespace TestFramework.Web.Sql.Model;

/// <summary>
/// Registers explicit model mappings.
/// </summary>
public sealed class SqlModelBuilder
{
    private readonly Dictionary<Type, SqlModelDefinition> _definitions = [];

    /// <summary>
    /// Starts mapping a model type.
    /// </summary>
    /// <typeparam name="TModel">The model type to map.</typeparam>
    public SqlModelBuilder<TModel> For<TModel>()
        where TModel : class
    {
        if (!_definitions.TryGetValue(typeof(TModel), out SqlModelDefinition? definition))
        {
            definition = new SqlModelDefinition(typeof(TModel));
            _definitions[typeof(TModel)] = definition;
        }

        return new SqlModelBuilder<TModel>(definition);
    }

    internal IReadOnlyDictionary<Type, SqlModelDefinition> Definitions => _definitions;
}

/// <summary>
/// Configures the mapping of a single model type.
/// </summary>
/// <typeparam name="TModel">The model type being mapped.</typeparam>
public sealed class SqlModelBuilder<TModel>(SqlModelDefinition definition)
    where TModel : class
{
    /// <summary>
    /// Sets the schema.
    /// </summary>
    /// <param name="schema">The schema name.</param>
    public SqlModelBuilder<TModel> Schema(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        definition.Schema = schema;
        return this;
    }

    /// <summary>
    /// Sets the table name.
    /// </summary>
    /// <param name="table">The table name.</param>
    public SqlModelBuilder<TModel> Table(string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        definition.Table = table;
        return this;
    }

    /// <summary>
    /// Declares a property as part of the primary key. Call once per key column, in key order.
    /// </summary>
    /// <param name="property">The key property.</param>
    public SqlModelBuilder<TModel> Key(Expression<Func<TModel, object?>> property)
    {
        definition.AddKey(ResolveProperty(property));
        return this;
    }

    /// <summary>
    /// Maps a property to a column name that differs from the property name.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="columnName">The column name.</param>
    public SqlModelBuilder<TModel> Column(Expression<Func<TModel, object?>> property, string columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        definition.SetColumnName(ResolveProperty(property), columnName);
        return this;
    }

    /// <summary>
    /// Declares a property as assigned by the database, so it is never written and is read back after insert.
    /// </summary>
    /// <param name="property">The generated property.</param>
    public SqlModelBuilder<TModel> Generated(Expression<Func<TModel, object?>> property)
    {
        definition.AddGenerated(ResolveProperty(property));
        return this;
    }

    /// <summary>
    /// Excludes a property from the mapping entirely.
    /// </summary>
    /// <param name="property">The property to ignore.</param>
    public SqlModelBuilder<TModel> Ignore(Expression<Func<TModel, object?>> property)
    {
        definition.AddIgnored(ResolveProperty(property));
        return this;
    }

    private static PropertyInfo ResolveProperty(Expression<Func<TModel, object?>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        Expression body = expression.Body is UnaryExpression unary ? unary.Operand : expression.Body;
        if (body is MemberExpression { Member: PropertyInfo property })
            return property;

        throw SqlModelMapException.NotAProperty(typeof(TModel), expression.ToString());
    }
}

/// <summary>
/// Accumulated explicit mapping instructions for one model type.
/// </summary>
public sealed class SqlModelDefinition(Type modelType)
{
    private readonly List<PropertyInfo> _keys = [];
    private readonly HashSet<PropertyInfo> _generated = [];
    private readonly HashSet<PropertyInfo> _ignored = [];
    private readonly Dictionary<PropertyInfo, string> _columnNames = [];

    internal Type ModelType { get; } = modelType;

    internal string? Schema { get; set; }

    internal string? Table { get; set; }

    internal void AddKey(PropertyInfo property)
    {
        if (!_keys.Contains(property))
            _keys.Add(property);
    }

    internal void AddGenerated(PropertyInfo property) => _generated.Add(property);

    internal void AddIgnored(PropertyInfo property) => _ignored.Add(property);

    internal void SetColumnName(PropertyInfo property, string columnName) => _columnNames[property] = columnName;

    internal SqlModelMap Build()
    {
        List<SqlColumnMap> columns = [];
        foreach (PropertyInfo property in SqlModelReflection.GetMappableProperties(ModelType))
        {
            if (_ignored.Contains(property))
                continue;

            columns.Add(new SqlColumnMap(
                property,
                _columnNames.TryGetValue(property, out string? name) ? name : property.Name,
                _keys.Contains(property),
                _generated.Contains(property)));
        }

        // Key order follows the order the keys were declared, which is the order the caller passes
        // key values in.
        List<SqlColumnMap> ordered = [
            .. _keys.Select(key => columns.First(column => column.Property == key)),
            .. columns.Where(column => !column.IsKey),
        ];

        return new SqlModelMap(ModelType, Schema, Table ?? ModelType.Name, ordered);
    }
}
