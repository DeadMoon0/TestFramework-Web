using System;
using System.Collections.Generic;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web.Sql.Exceptions;

/// <summary>
/// Thrown when a table definition cannot be derived from a model map.
/// </summary>
/// <remarks>
/// Generation refuses to guess. A column it cannot describe faithfully fails here instead of
/// producing a table that silently differs from the model.
/// </remarks>
public sealed class SqlSchemaGenerationException : TimelineFrameworkException
{
    /// <summary>
    /// Creates an exception for a property whose type has no SQL Server equivalent.
    /// </summary>
    /// <param name="modelType">The model type being generated.</param>
    /// <param name="columnName">The column that could not be typed.</param>
    /// <param name="clrType">The unsupported CLR type.</param>
    /// <returns>The exception describing the unsupported type.</returns>
    public static SqlSchemaGenerationException UnsupportedType(Type modelType, string columnName, Type clrType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(clrType);

        return new SqlSchemaGenerationException(
            $"No SQL Server type could be derived for '{modelType.Name}.{columnName}' of type {clrType.Name}.",
            [
                $"Declare the type: models.For<{modelType.Name}>().ColumnType(x => x.{columnName}, \"...\").",
                "Or annotate the property with [Column(TypeName = \"...\")].",
                "Or write the table by hand and pass it as a schema script instead.",
            ]);
    }

    /// <summary>
    /// Creates an exception for a database-assigned column whose default cannot be derived.
    /// </summary>
    /// <param name="modelType">The model type being generated.</param>
    /// <param name="columnName">The column that could not be described.</param>
    /// <param name="clrType">The CLR type of the column.</param>
    /// <returns>The exception describing the undeterminable default.</returns>
    public static SqlSchemaGenerationException UndeterminableGeneratedValue(Type modelType, string columnName, Type clrType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(clrType);

        return new SqlSchemaGenerationException(
            $"'{modelType.Name}.{columnName}' is database-assigned, but no default could be derived for a {clrType.Name} column.",
            [
                "An integer column becomes IDENTITY, a Guid gets NEWSEQUENTIALID and a timestamp gets SYSUTCDATETIME.",
                "For anything else, write the table by hand and pass it as a schema script.",
            ]);
    }

    /// <summary>
    /// Creates an exception for an identity column whose type cannot carry one.
    /// </summary>
    /// <param name="modelType">The model type being generated.</param>
    /// <param name="columnName">The offending column.</param>
    /// <param name="clrType">The CLR type of the column.</param>
    /// <returns>The exception describing the invalid identity column.</returns>
    public static SqlSchemaGenerationException NonIntegerIdentity(Type modelType, string columnName, Type clrType)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(clrType);

        return new SqlSchemaGenerationException(
            $"'{modelType.Name}.{columnName}' is declared as an identity column, but SQL Server identities must be integers, not {clrType.Name}.",
            [
                "Use an integer property for the identity column.",
                $"Or drop the Identity declaration and mark it Generated, so a default is used instead.",
            ]);
    }

    /// <summary>
    /// Creates an exception for two models that generate the same table.
    /// </summary>
    /// <param name="qualifiedTable">The table both models map to.</param>
    /// <param name="firstModel">The first model.</param>
    /// <param name="secondModel">The conflicting model.</param>
    /// <returns>The exception describing the collision.</returns>
    public static SqlSchemaGenerationException DuplicateTable(string qualifiedTable, Type firstModel, Type secondModel)
    {
        ArgumentNullException.ThrowIfNull(firstModel);
        ArgumentNullException.ThrowIfNull(secondModel);

        return new SqlSchemaGenerationException(
            $"'{firstModel.Name}' and '{secondModel.Name}' both map to the table {qualifiedTable}, so only one of them can define it.",
            [
                "Map one of the models to a different table.",
                "Or generate the schema from only one of them.",
            ]);
    }

    private SqlSchemaGenerationException(string friendlyMessage, IReadOnlyList<string> recoverySteps, IReadOnlyList<string>? availableOptions = null, Exception? innerException = null)
        : base(friendlyMessage, recoverySteps, availableOptions, innerException)
    {
    }
}
