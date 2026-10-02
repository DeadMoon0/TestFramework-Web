using System;

namespace TestFramework.Web.Sql;

/// <summary>
/// Turns a configured SQL timeout into the whole seconds the SQL client takes.
/// </summary>
internal static class SqlTimeouts
{
    /// <summary>
    /// The SQL client's value for a command timeout. Without one, <c>0</c> - no limit of its own - so the step's
    /// deadline is the one that governs, as it is for an HTTP call; leaving it out instead left the client's hidden
    /// 30 s default in charge, which the configuration's documentation said was not there.
    /// </summary>
    /// <param name="configured">The configured timeout, or <see langword="null"/>.</param>
    /// <returns>Whole seconds, rounded up; <c>0</c> for none.</returns>
    internal static int CommandSeconds(TimeSpan? configured) => configured is { } timeout ? WholeSeconds(timeout) : 0;

    /// <summary>
    /// Whole seconds for a configured timeout, rounded up and never below one.
    /// </summary>
    /// <remarks>
    /// Truncating would turn anything under a second into <c>0</c>, which the SQL client reads as "wait forever" - the
    /// opposite of what a small timeout asks for.
    /// </remarks>
    /// <param name="timeout">The configured timeout.</param>
    /// <returns>The seconds.</returns>
    internal static int WholeSeconds(TimeSpan timeout) => Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
}
