using System;
using System.Collections.Generic;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web.Sql.Exceptions;

/// <summary>
/// Thrown when a model cannot be mapped to a table, key or column.
/// </summary>
public sealed class SqlModelMapException : TimelineFrameworkException
{
    /// <summary>
    /// Creates an exception for a model whose key columns could not be determined.
    /// </summary>
    /// <param name="modelType">The model type being mapped.</param>
    /// <returns>The exception describing the missing key.</returns>
    public static SqlModelMapException NoKey(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        return new SqlModelMapException(
            $"No key column could be determined for '{modelType.Name}'.",
            [
                $"Register it: AddWebSqlModels(models => models.For<{modelType.Name}>().Key(x => x.Id)).",
                $"Or annotate the key property of '{modelType.Name}' with [Key].",
                $"Or name the key property 'Id' or '{modelType.Name}Id' so the convention finds it.",
            ]);
    }

    /// <summary>
    /// Creates an exception for a model that has no mappable properties.
    /// </summary>
    /// <param name="modelType">The model type being mapped.</param>
    /// <returns>The exception describing the empty mapping.</returns>
    public static SqlModelMapException NoColumns(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        return new SqlModelMapException(
            $"'{modelType.Name}' has no public readable properties to map to columns.",
            ["Use a type with public properties for the row model, or register the columns explicitly."]);
    }

    /// <summary>
    /// Creates an exception for an expression that does not point at a property.
    /// </summary>
    /// <param name="modelType">The model type being mapped.</param>
    /// <param name="expression">The offending expression text.</param>
    /// <returns>The exception describing the unusable expression.</returns>
    public static SqlModelMapException NotAProperty(Type modelType, string expression)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        return new SqlModelMapException(
            $"The expression '{expression}' on '{modelType.Name}' does not point at a property.",
            ["Use a simple property access such as x => x.Id."]);
    }

    /// <summary>
    /// Creates an exception for a key value that cannot be converted to the mapped column type.
    /// </summary>
    /// <param name="modelType">The model type being mapped.</param>
    /// <param name="columnName">The key column.</param>
    /// <param name="value">The offending value.</param>
    /// <param name="targetType">The type the value had to convert to.</param>
    /// <param name="innerException">The underlying conversion failure.</param>
    /// <returns>The exception describing the conversion failure.</returns>
    public static SqlModelMapException KeyConversionFailed(Type modelType, string columnName, string value, Type targetType, Exception innerException)
        => new(
            $"The key value '{value}' for '{modelType.Name}.{columnName}' could not be converted to {targetType.Name}.",
            [$"Pass a value that parses as {targetType.Name}."],
            null,
            innerException);

    private SqlModelMapException(string friendlyMessage, IReadOnlyList<string> recoverySteps, IReadOnlyList<string>? availableOptions = null, Exception? innerException = null)
        : base(friendlyMessage, recoverySteps, availableOptions, innerException)
    {
    }
}
