using System;
using System.Globalization;

namespace TestFramework.Web.Configuration;

/// <summary>
/// What counts as a valid configuration value, asked the same way from both directions.
/// </summary>
/// <remarks>
/// <para>
/// Every resource has two readers - the file provider, and the shape that rebuilds a record from the run's
/// values - and each value's rule used to be written in both. They had already drifted: a timeout the
/// provider refused as non-positive was accepted by the shape, so the same wrong value was a load-time
/// sentence when a person wrote it and a raw <c>ArgumentOutOfRangeException</c> mid-run when an environment
/// component published it. A rule two readers must agree on lives here, once; what each reader <em>says</em>
/// about a violation stays its own, because the two failures happen at different times and owe different
/// recovery steps.
/// </para>
/// </remarks>
internal static class ConfigValueRules
{
    /// <summary>
    /// Whether the text is a duration a timeout or interval can actually run under.
    /// </summary>
    /// <remarks>
    /// Positive, not merely parseable: zero and negative values do not bound anything - a zero poll
    /// interval is a hot loop, a zero client timeout is an argument exception - so "readable but
    /// meaningless" is refused the same as "unreadable".
    /// </remarks>
    /// <param name="value">The text.</param>
    /// <param name="parsed">The duration, when the text holds one.</param>
    /// <returns>True when the text is a positive duration.</returns>
    internal static bool PositiveDuration(string? value, out TimeSpan parsed)
    {
        parsed = default;

        return value is { Length: > 0 }
            && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out parsed)
            && parsed > TimeSpan.Zero;
    }

    /// <summary>
    /// Whether the text names a value the enum actually has.
    /// </summary>
    /// <remarks>
    /// Defined, not merely parseable: <c>Enum.TryParse</c> accepts any integer text, so a typo like
    /// <c>"7"</c> used to become a mode nothing handles - which for an auth mode meant requests going out
    /// unauthenticated. A name the enum does not know is refused, and the caller lists the ones it does.
    /// </remarks>
    /// <typeparam name="TEnum">The enum.</typeparam>
    /// <param name="value">The text.</param>
    /// <param name="parsed">The value, when the text names one.</param>
    /// <returns>True when the text names a defined value.</returns>
    internal static bool DefinedEnum<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;

        return value is { Length: > 0 }
            && Enum.TryParse(value, ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed);
    }
}
