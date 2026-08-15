using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Variables;
using TestFramework.Web.Sql.Execution;

namespace TestFramework.Web.Sql.Steps;

/// <summary>
/// A script to run against a database.
/// </summary>
public sealed class SqlScript
{
    private SqlScript(string text, string description)
    {
        Text = text;
        Description = description;
    }

    /// <summary>
    /// The script text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// A short description used in logs, such as the file name.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Creates a script from inline text.
    /// </summary>
    /// <param name="text">The script text.</param>
    /// <param name="description">A short description used in logs. Defaults to a generic one.</param>
    public static SqlScript FromText(string text, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new SqlScript(text, string.IsNullOrWhiteSpace(description) ? "inline script" : description);
    }

    /// <summary>
    /// Creates a script from a file.
    /// </summary>
    /// <param name="path">The path to the script file.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static SqlScript FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"The SQL script '{fullPath}' does not exist. Check the path and that the file is copied to the output directory.", fullPath);

        return new SqlScript(File.ReadAllText(fullPath), Path.GetFileName(fullPath));
    }

    // A GO line may carry a repeat count and a trailing line comment, both of which SSMS and sqlcmd
    // accept. Anchored per line so a GO inside a string literal or a block comment is left alone.
    private static readonly Regex BatchSeparator = new(
        @"^[ \t]*GO(?:[ \t]+(?<count>\d+))?[ \t]*(?:--[^\r\n]*)?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Splits the script on <c>GO</c> batch separators.
    /// </summary>
    /// <remarks>
    /// <c>GO</c> is a client-side separator, not a statement, so it must be removed before the text
    /// reaches the server. The repeat form <c>GO 3</c> is honoured by emitting the batch three
    /// times, and a trailing <c>-- comment</c> on the separator line does not stop it being one.
    /// </remarks>
    public IReadOnlyList<string> SplitBatches()
    {
        List<string> batches = [];
        int start = 0;

        foreach (Match separator in BatchSeparator.Matches(Text))
        {
            AddBatch(batches, Text[start..separator.Index], separator.Groups["count"].Value);
            start = separator.Index + separator.Length;
        }

        AddBatch(batches, Text[start..], string.Empty);
        return batches;
    }

    private static void AddBatch(List<string> batches, string text, string repeatCount)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
            return;

        int repeats = int.TryParse(repeatCount, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : 1;

        for (int index = 0; index < repeats; index++)
            batches.Add(trimmed);
    }

    /// <summary>
    /// Returns the script description.
    /// </summary>
    public override string ToString() => Description;
}

/// <summary>
/// Result of running a script.
/// </summary>
/// <param name="SqlIdentifier">The database the script ran against.</param>
/// <param name="Batches">The number of batches executed.</param>
/// <param name="AffectedRows">The total number of affected rows reported by the batches.</param>
/// <param name="Elapsed">Wall-clock duration of the script.</param>
public sealed record SqlScriptResult(string SqlIdentifier, int Batches, int AffectedRows, TimeSpan Elapsed) : StepResultContext
{
    /// <summary>
    /// Returns a readable description of the outcome.
    /// </summary>
    public override string ToString() => $"'{SqlIdentifier}' ran {Batches} batch(es) affecting {AffectedRows} row(s) in {Elapsed:g}";
}

/// <summary>
/// Runs a script, batch by batch.
/// </summary>
public sealed class SqlScriptStep : SqlStepBase<SqlScriptResult>
{
    private readonly SqlScript _script;

    /// <summary>
    /// Creates the step.
    /// </summary>
    /// <param name="sqlIdentifier">The SQL identifier to run against.</param>
    /// <param name="script">The script to run.</param>
    /// <param name="parameters">The variable-backed parameters shared by every batch.</param>
    public SqlScriptStep(SqlIdentifier sqlIdentifier, SqlScript script, SqlParameterSet? parameters = null)
        : base(sqlIdentifier, RequireScript(script).Text, parameters ?? new SqlParameterSet())
    {
        _script = script;
    }

    /// <inheritdoc />
    public override string Name => "SQL Script";

    /// <inheritdoc />
    public override string Description => $"Runs '{_script.Description}' against the database '{SqlIdentifier}'";

    /// <summary>
    /// Binds a parameter available to every batch of the script.
    /// </summary>
    /// <typeparam name="TValue">The parameter value type.</typeparam>
    /// <param name="name">The parameter name, without the leading marker.</param>
    /// <param name="value">The variable carrying the value.</param>
    public SqlScriptStep WithParameter<TValue>(string name, VariableReference<TValue> value)
    {
        ((TestFramework.Core.IFreezable)this).EnsureNotFrozen();
        Parameters.Add(name, value);
        return this;
    }

    /// <inheritdoc />
    public override Step<SqlScriptResult> Clone()
        => new SqlScriptStep(SqlIdentifier, _script, Parameters.Clone()).WithClonedOptions(this);

    /// <inheritdoc />
    public override StepInstance<Step<SqlScriptResult>, SqlScriptResult> GetInstance() => new(this);

    /// <inheritdoc />
    public override async Task<SqlScriptResult?> Execute(
        IServiceProvider serviceProvider,
        VariableStore variableStore,
        ArtifactStore artifactStore,
        ScopedLogger logger,
        CancellationToken cancellationToken)
    {
        (ISqlExecutor executor, IReadOnlyDictionary<string, object?> parameters) = Prepare(serviceProvider, variableStore, logger);

        IReadOnlyList<string> batches = _script.SplitBatches();
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // One call, not one per batch: the batches of a script share connection state, and splitting
        // them across connections would drop every #temp table and SET option between them.
        SqlScriptExecutionResult execution = await executor
            .ExecuteScriptAsync(SqlIdentifier, batches, parameters, cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();
        logger.LogInformation("SQL script '{0}' ran {1} batch(es) on '{2}' in {3}", _script.Description, batches.Count, SqlIdentifier.ToString(), stopwatch.Elapsed);

        // An unbalanced BEGIN TRAN rolls back silently when the connection closes, so the script
        // would appear to have succeeded while having changed nothing.
        if (execution.OpenTransactionCount is > 0 and { } openTransactions)
        {
            logger.LogWarning(
                $"SQL script '{_script.Description}' left {openTransactions} transaction(s) open on '{SqlIdentifier}'. "
                + "Closing the connection rolls them back without an error, so the script's changes are discarded. "
                + "Add the matching COMMIT or ROLLBACK.");
        }

        return new SqlScriptResult(SqlIdentifier, batches.Count, execution.AffectedRows, stopwatch.Elapsed);
    }

    private static SqlScript RequireScript(SqlScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return script;
    }
}
