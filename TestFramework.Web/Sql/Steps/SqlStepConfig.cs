namespace TestFramework.Web.Sql.Steps;

/// <summary>
/// Run-level tuning for SQL steps.
/// </summary>
public sealed record SqlStepConfig
{
    /// <summary>
    /// Logs the statement text and parameter names for every SQL step.
    /// </summary>
    public bool LogStatements { get; init; } = true;

    /// <summary>
    /// Logs parameter values as well as names.
    /// </summary>
    /// <remarks>
    /// Off by default: parameter values are real data and routinely contain personal or sensitive
    /// information. Turn it on deliberately while diagnosing a query.
    /// </remarks>
    public bool LogParameterValues { get; init; }
}
