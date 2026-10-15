using System;
using TestFramework.Core.Exceptions;

namespace TestFramework.Web;

/// <summary>
/// The refusal every value verb gives when it is handed a variable, and what the compiler says before that.
/// </summary>
/// <remarks>
/// Each value verb - <c>WithParameter</c>, <c>WithJsonBody</c> - takes the plain value, and its <c>…From</c> twin
/// takes a variable. A variable is never a value: stored as data it could only be read later by whatever unwrapped
/// it, which the plan check cannot see. The hidden overloads that make this a compile error carry
/// <see cref="Message"/>; this refusal covers a variable that reaches the value verb anyway, behind a caller's own
/// generic helper.
/// </remarks>
internal static class NotAValue
{
    /// <summary>What the compiler says when a variable is passed to a value verb.</summary>
    public const string Message = "A variable is not a value: this verb takes the value itself. Use its ...From twin, such as WithParameterFrom or WithJsonBodyFrom, to read a variable.";

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
