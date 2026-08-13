using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Web.Sql.Model;

namespace TestFramework.Web.Sql.Execution;

/// <summary>
/// A statement and the parameter names it expects.
/// </summary>
/// <param name="Text">The statement text.</param>
/// <param name="ParameterNames">The parameter names, without the leading marker.</param>
public sealed record SqlStatement(string Text, IReadOnlyList<string> ParameterNames)
{
    /// <summary>
    /// Returns the statement text.
    /// </summary>
    public override string ToString() => Text;
}

/// <summary>
/// Builds the statements the artifact and step surfaces need.
/// </summary>
/// <remarks>
/// Table and column names are bracket-quoted and always come from the model map. Values are always
/// parameters, so no caller-supplied value is ever concatenated into a statement.
/// </remarks>
public static class SqlStatementBuilder
{
    /// <summary>
    /// Prefix used for the parameters this builder generates.
    /// </summary>
    public const string KeyParameterPrefix = "tf_key";

    /// <summary>
    /// Prefix used for the value parameters this builder generates.
    /// </summary>
    public const string ValueParameterPrefix = "tf_val";

    /// <summary>
    /// Builds a select of every column for one row addressed by its key.
    /// </summary>
    /// <param name="map">The model map.</param>
    public static SqlStatement SelectByKey(SqlModelMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        string columns = string.Join(", ", map.Columns.Select(FormatColumnWithAlias));
        return new SqlStatement(
            $"SELECT {columns} FROM {map.QualifiedTable} WHERE {BuildKeyPredicate(map)};",
            [.. KeyParameterNames(map)]);
    }

    /// <summary>
    /// Builds a select of every column for the rows matching a where clause.
    /// </summary>
    /// <param name="map">The model map.</param>
    /// <param name="whereClause">The predicate, without the <c>WHERE</c> keyword.</param>
    /// <param name="parameterNames">The parameter names the predicate uses.</param>
    public static SqlStatement SelectWhere(SqlModelMap map, string whereClause, IEnumerable<string> parameterNames)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(whereClause);

        string columns = string.Join(", ", map.Columns.Select(FormatColumnWithAlias));
        return new SqlStatement(
            $"SELECT {columns} FROM {map.QualifiedTable} WHERE {whereClause};",
            [.. parameterNames]);
    }

    /// <summary>
    /// Builds an existence check for one row addressed by its key.
    /// </summary>
    /// <param name="map">The model map.</param>
    public static SqlStatement ExistsByKey(SqlModelMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return new SqlStatement(
            $"SELECT COUNT(1) FROM {map.QualifiedTable} WHERE {BuildKeyPredicate(map)};",
            [.. KeyParameterNames(map)]);
    }

    /// <summary>
    /// Builds an insert of every writable column.
    /// </summary>
    /// <param name="map">The model map.</param>
    public static SqlStatement Insert(SqlModelMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        string columns = string.Join(", ", map.WritableColumns.Select(column => SqlModelMap.Quote(column.ColumnName)));
        string values = string.Join(", ", map.WritableColumns.Select((_, index) => $"@{ValueParameterPrefix}{index}"));

        return new SqlStatement(
            $"INSERT INTO {map.QualifiedTable} ({columns}) VALUES ({values});",
            [.. map.WritableColumns.Select((_, index) => $"{ValueParameterPrefix}{index}")]);
    }

    /// <summary>
    /// Builds an update of every writable non-key column for one row addressed by its key.
    /// </summary>
    /// <param name="map">The model map.</param>
    public static SqlStatement UpdateByKey(SqlModelMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        SqlColumnMap[] assignable = [.. map.WritableColumns.Where(column => !column.IsKey)];
        if (assignable.Length == 0)
        {
            // A table that is nothing but its key has nothing to update; the row either exists or is
            // inserted.
            return new SqlStatement(string.Empty, []);
        }

        string assignments = string.Join(", ", assignable.Select((column, index) => $"{SqlModelMap.Quote(column.ColumnName)} = @{ValueParameterPrefix}{index}"));
        return new SqlStatement(
            $"UPDATE {map.QualifiedTable} SET {assignments} WHERE {BuildKeyPredicate(map)};",
            [
                .. assignable.Select((_, index) => $"{ValueParameterPrefix}{index}"),
                .. KeyParameterNames(map),
            ]);
    }

    /// <summary>
    /// Builds a delete of one row addressed by its key.
    /// </summary>
    /// <param name="map">The model map.</param>
    public static SqlStatement DeleteByKey(SqlModelMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return new SqlStatement(
            $"DELETE FROM {map.QualifiedTable} WHERE {BuildKeyPredicate(map)};",
            [.. KeyParameterNames(map)]);
    }

    /// <summary>
    /// Returns the parameter names used for the key columns, in key order.
    /// </summary>
    /// <param name="map">The model map.</param>
    public static IEnumerable<string> KeyParameterNames(SqlModelMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.KeyColumns.Select((_, index) => $"{KeyParameterPrefix}{index}");
    }

    private static string BuildKeyPredicate(SqlModelMap map)
        => string.Join(" AND ", map.KeyColumns.Select((column, index) => $"{SqlModelMap.Quote(column.ColumnName)} = @{KeyParameterPrefix}{index}"));

    private static string FormatColumnWithAlias(SqlColumnMap column)
        => string.Equals(column.ColumnName, column.Property.Name, StringComparison.Ordinal)
            ? SqlModelMap.Quote(column.ColumnName)
            : $"{SqlModelMap.Quote(column.ColumnName)} AS {SqlModelMap.Quote(column.Property.Name)}";
}
