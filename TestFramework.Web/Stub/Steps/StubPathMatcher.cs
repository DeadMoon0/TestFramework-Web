using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace TestFramework.Web.Stub.Steps;

/// <summary>
/// Matches a logged request path against a filter pattern, with the same <c>*</c> wildcard the
/// mappings use.
/// </summary>
/// <remarks>
/// A mapping is declared with <c>/api/charges/*</c> and happily answers the call. Filtering the
/// request log with the very same string has to find it, or the two halves of the stub surface
/// disagree about what a path is. A pattern with no <c>*</c> stays an ordinary comparison, so this
/// only ever widens what an existing filter matches.
/// </remarks>
internal static class StubPathMatcher
{
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns whether a logged path satisfies a filter pattern.
    /// </summary>
    /// <param name="path">The path as the stub logged it.</param>
    /// <param name="pattern">The filter pattern, where <c>*</c> matches any run of characters.</param>
    public static bool Matches(string? path, string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (path is null)
            return false;

        if (!pattern.Contains('*', StringComparison.Ordinal))
            return string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase);

        return Patterns.GetOrAdd(pattern, Compile).IsMatch(path);
    }

    /// <summary>
    /// Adds the leading slash a logged path always has, so both spellings of a filter work.
    /// </summary>
    /// <param name="path">The path as the caller wrote it, or <see langword="null"/> for no filter.</param>
    public static string? Normalize(string? path)
        => path is null || path.StartsWith('/') ? path : $"/{path}";

    private static Regex Compile(string pattern)
    {
        // Escape everything that is not the wildcard, so a path with a '.' or '+' in it cannot turn
        // into a regex of its own.
        string[] segments = pattern.Split('*');
        StringBuilder expression = new("^");

        for (int index = 0; index < segments.Length; index++)
        {
            if (index > 0)
                expression.Append(".*");

            expression.Append(Regex.Escape(segments[index]));
        }

        expression.Append('$');

        return new Regex(
            expression.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }
}
