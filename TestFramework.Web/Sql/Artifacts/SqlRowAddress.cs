using System;
using System.Collections.Generic;
using System.Linq;
using TestFramework.Web.Sql.Model;

namespace TestFramework.Web.Sql.Artifacts;

/// <summary>
/// Which row a row reference addresses, and how it came to know.
/// </summary>
/// <remarks>
/// Three states rather than a list of key values that may be empty, because each one is a different answer to
/// "may setup write this row, and what does teardown delete": a row the test named, a row the database is about
/// to number, and a row the database numbered for this run.
/// </remarks>
internal abstract record SqlRowAddress
{
    private SqlRowAddress() { }

    /// <summary>
    /// The value the test gave for a key column the test supplies.
    /// </summary>
    /// <param name="map">The model map.</param>
    /// <param name="column">A key column the database does not assign.</param>
    /// <returns>The value as text.</returns>
    internal abstract string SuppliedValueFor(SqlModelMap map, SqlColumnMap column);

    /// <summary>
    /// Every key value was given, so it addresses a row that exists or that setup writes under that key.
    /// </summary>
    /// <param name="Values">The key values, in key order.</param>
    internal sealed record Given(IReadOnlyList<string> Values) : SqlRowAddress
    {
        internal override string SuppliedValueFor(SqlModelMap map, SqlColumnMap column) => Values[IndexOf(map.KeyColumns, column)];
    }

    /// <summary>
    /// The key columns the database assigns were left out, so setup creates the row and the address waits for it.
    /// </summary>
    /// <param name="Supplied">The values of the key columns the test supplies, in key order.</param>
    internal sealed record AwaitingGeneratedKey(IReadOnlyList<string> Supplied) : SqlRowAddress
    {
        internal override string SuppliedValueFor(SqlModelMap map, SqlColumnMap column)
            => Supplied[IndexOf([.. map.KeyColumns.Where(key => !key.IsGenerated)], column)];
    }

    /// <summary>
    /// Setup created the row and the database numbered it: the run owns exactly that row.
    /// </summary>
    /// <param name="Values">The key values the database reported, in key order.</param>
    internal sealed record Assigned(IReadOnlyList<string> Values) : SqlRowAddress
    {
        internal override string SuppliedValueFor(SqlModelMap map, SqlColumnMap column) => Values[IndexOf(map.KeyColumns, column)];
    }

    private static int IndexOf(IReadOnlyList<SqlColumnMap> columns, SqlColumnMap column)
    {
        for (int index = 0; index < columns.Count; index++)
        {
            if (string.Equals(columns[index].Property.Name, column.Property.Name, StringComparison.Ordinal))
                return index;
        }

        throw new ArgumentException($"'{column.ColumnName}' is not among the columns the address holds.", nameof(column));
    }
}
