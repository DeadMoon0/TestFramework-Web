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
    /// Creates an exception for a property declared as a key and also ignored.
    /// </summary>
    /// <remarks>
    /// The two calls contradict each other and only the author knows which was meant, so the model is
    /// refused by name - the alternative was a bare LINQ exception from the lookup that could not find
    /// the ignored key among the mapped columns.
    /// </remarks>
    /// <param name="modelType">The model type being mapped.</param>
    /// <param name="key">The property that is both key and ignored.</param>
    /// <returns>The exception describing the contradiction.</returns>
    public static SqlModelMapException KeyIgnored(Type modelType, string key)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return new SqlModelMapException(
            $"'{modelType.Name}.{key}' is declared as a key and also ignored, so the model cannot be mapped.",
            [
                $"Remove either .Key(x => x.{key}) or .Ignore(x => x.{key}) - a key column cannot be left unmapped.",
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

    /// <summary>
    /// Creates an exception for a row reference given a number of key values that addresses neither a row that
    /// exists nor one setup creates.
    /// </summary>
    /// <param name="modelType">The model type.</param>
    /// <param name="supplied">How many key values were given.</param>
    /// <param name="keyColumns">Every key column, in key order.</param>
    /// <param name="generatedKeyColumns">The key columns the database assigns.</param>
    /// <returns>The exception describing the count.</returns>
    internal static SqlModelMapException KeyValueCount(Type modelType, int supplied, IReadOnlyList<string> keyColumns, IReadOnlyList<string> generatedKeyColumns)
    {
        string order = string.Join(", ", keyColumns);

        List<string> recoverySteps = [$"Give every key value, in key order ({order}), to address a row that exists."];
        if (generatedKeyColumns.Count > 0)
        {
            int suppliedByTheTest = keyColumns.Count - generatedKeyColumns.Count;
            recoverySteps.Add($"Or give only the {suppliedByTheTest} the test supplies and leave out {string.Join(", ", generatedKeyColumns)}, which the database assigns, so setup creates the row and reads the key back.");
        }

        return new SqlModelMapException(
            $"'{modelType.Name}' maps to {keyColumns.Count} key column(s) ({order}) but {supplied} key value(s) were given.",
            recoverySteps);
    }

    /// <summary>
    /// Creates an exception for a row the test creates whose reference names a key the database assigns.
    /// </summary>
    /// <remarks>
    /// The insert leaves a generated column out, so the database numbers the row itself and the given value
    /// addresses a different row - one that is not there. Teardown then deletes nothing and the row the run
    /// created stays behind, which is why this is refused rather than tried.
    /// </remarks>
    /// <param name="modelType">The model type.</param>
    /// <param name="generatedKeyColumns">The key columns the database assigns.</param>
    /// <returns>The exception describing the conflict.</returns>
    internal static SqlModelMapException GeneratedKeyGiven(Type modelType, IReadOnlyList<string> generatedKeyColumns)
    {
        string generated = string.Join(", ", generatedKeyColumns);

        return new SqlModelMapException(
            $"'{modelType.Name}' is set up with a value for {generated}, which the database assigns, so the row it inserts would not be the row the reference addresses.",
            [
                $"Leave {generated} out of the reference - Row<{modelType.Name}>(identifier) - so setup reads the assigned key back; chain .KeyTo(...) to hand it to later steps.",
                $"To adopt a row that already exists, register it instead: RegisterArtifact(name, Row<{modelType.Name}>(identifier, keys...)).",
            ]);
    }

    /// <summary>
    /// Creates an exception for seed data whose key disagrees with the reference.
    /// </summary>
    /// <param name="modelType">The model type.</param>
    /// <param name="columnName">The key column.</param>
    /// <param name="referenceValue">The value the reference addresses.</param>
    /// <param name="rowValue">The value the seed row carries.</param>
    /// <returns>The exception describing the disagreement.</returns>
    internal static SqlModelMapException SeedKeyDisagrees(Type modelType, string columnName, string referenceValue, string? rowValue)
        => new(
            $"The '{modelType.Name}' row being set up carries {columnName} = '{rowValue}', and its reference addresses {columnName} = '{referenceValue}'; teardown would delete a row setup never wrote.",
            [$"Give the reference and the row the same {columnName}."]);

    /// <summary>
    /// Creates an exception for a <c>KeyTo</c> naming a property that is not a key column.
    /// </summary>
    /// <param name="modelType">The model type.</param>
    /// <param name="propertyName">The property.</param>
    /// <param name="keyProperties">The key properties.</param>
    /// <returns>The exception describing the property.</returns>
    internal static SqlModelMapException NotAKey(Type modelType, string propertyName, IReadOnlyList<string> keyProperties)
        => new(
            $"KeyTo names '{modelType.Name}.{propertyName}', which is not a key column.",
            [$"Name a key property: {string.Join(", ", keyProperties)}."]);

    private SqlModelMapException(string friendlyMessage, IReadOnlyList<string> recoverySteps, IReadOnlyList<string>? availableOptions = null, Exception? innerException = null)
        : base(friendlyMessage, recoverySteps, availableOptions ?? [], innerException)
    {
    }
}
