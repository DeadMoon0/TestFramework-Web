using System;
using Xunit;

namespace TestFramework.Web.Tests.Shared;

/// <summary>
/// Decides which of this suite's tests can run in the environment they were started in.
/// </summary>
/// <remarks>
/// A fresh clone must go green on a bare <c>dotnet test</c>. Everything that needs an external
/// service therefore opts in through an environment variable and skips — visibly — when it is
/// absent, rather than failing and making the suite look broken to someone who has just arrived.
/// </remarks>
internal static class WebTestEnvironmentGate
{
    /// <summary>
    /// Environment variable holding a SQL Server connection string for the round-trip tests.
    /// </summary>
    public const string SqlConnectionStringVariable = "TESTFRAMEWORK_WEB_SQL";

    /// <summary>
    /// Returns the configured SQL Server connection string, or <see langword="null"/> when there is none.
    /// </summary>
    public static string? SqlConnectionString
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable(SqlConnectionStringVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>
    /// Returns whether a SQL Server is configured, with the reason when it is not.
    /// </summary>
    /// <param name="reason">The skip reason, or an empty string when the tests can run.</param>
    public static bool IsSqlServerConfigured(out string reason)
    {
        if (SqlConnectionString is not null)
        {
            reason = string.Empty;
            return true;
        }

        reason = $"Set {SqlConnectionStringVariable} to a SQL Server connection string to run the round-trip tests.";
        return false;
    }
}

/// <summary>
/// A fact that skips itself unless a SQL Server connection string is configured.
/// </summary>
/// <remarks>
/// The skip is decided in the constructor, which works on the pinned xunit 2.5.3 because
/// <see cref="FactAttribute.Skip"/> is a plain settable property read at discovery. xunit v3 changes
/// this: <c>Skip</c> becomes a static condition there and dynamic skipping moves to
/// <c>SkipWhen</c>/<c>SkipUnless</c> naming a static member, so this attribute must be revisited
/// with that upgrade rather than silently running the tests.
/// </remarks>
internal sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!WebTestEnvironmentGate.IsSqlServerConfigured(out string reason))
            Skip = reason;
    }
}
