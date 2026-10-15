using System;
using System.Linq;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;

namespace TestFramework.Web;

/// <summary>
/// The refusal every typed result accessor gives when a step produced something else.
/// </summary>
/// <remarks>
/// Asking a run for the wrong kind of result is a mistake in the test, not in the system under test, so it is a
/// configuration failure. Generic types are spelled out, because <c>SqlScalarResult`1</c> on both sides of the
/// message hides the one difference that matters.
/// </remarks>
internal static class StepResultMismatch
{
    /// <summary>
    /// Builds the refusal for a step whose last result is not the expected type.
    /// </summary>
    /// <param name="handle">The step that was asked.</param>
    /// <param name="expected">The result type the accessor needs.</param>
    /// <returns>The exception to throw.</returns>
    public static FrameworkConfigurationException For(StepHandle handle, Type expected)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(expected);

        object? actual = handle.LastResult.Result;
        return new FrameworkConfigurationException(
            $"Step '{handle.Label ?? handle.Step.Name}' did not produce a {NameOf(expected)}. "
            + $"Its last result was '{(actual is null ? "null" : NameOf(actual.GetType()))}'.");
    }

    /// <summary>
    /// A type's name with its generic arguments, such as <c>SqlScalarResult&lt;Int32&gt;</c>.
    /// </summary>
    /// <param name="type">The type to name.</param>
    /// <returns>The readable name.</returns>
    internal static string NameOf(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        string name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(NameOf))}>";
    }
}
