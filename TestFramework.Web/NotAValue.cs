using System;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web;

/// <summary>
/// The refusal every value verb gives when it is handed a variable.
/// </summary>
/// <remarks>
/// Each value verb - <c>WithParameter</c>, <c>WithJsonBody</c> - takes the plain value, and its <c>…From</c> twin
/// takes a variable. A variable is never a value: stored as data it could only be read later by whatever unwrapped
/// it, which the plan check cannot see, so a value verb refuses one while the timeline is built and names the twin.
/// </remarks>
internal static class NotAValue
{
    /// <summary>
    /// The refusal for a variable handed to a value verb.
    /// </summary>
    /// <param name="verb">The value verb, such as <c>WithParameter('id')</c>.</param>
    /// <param name="fromVerb">Its twin that reads a variable.</param>
    /// <returns>The exception to throw.</returns>
    public static FrameworkConfigurationException For(string verb, string fromVerb)
        => new($"{verb} was handed a variable, and takes the value itself.")
        {
            RecoverySteps = [$"Use {fromVerb} to read the variable when the step runs."],
        };

    /// <summary>
    /// Refuses a variable that reached a value verb as a plain value.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="verb">The value verb, for the refusal.</param>
    /// <param name="fromVerb">Its twin that reads a variable.</param>
    /// <returns>The value.</returns>
    public static T Require<T>(T value, string verb, string fromVerb)
        => value is TestFramework.Core.Variables.VariableReferenceGeneric ? throw For(verb, fromVerb) : value;
}
