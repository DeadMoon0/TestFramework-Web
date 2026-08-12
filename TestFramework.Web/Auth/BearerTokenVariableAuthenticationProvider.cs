using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TestFramework.Core.Variables;

namespace TestFramework.Web.Auth;

/// <summary>
/// Sends a bearer token resolved from a variable at run time.
/// </summary>
/// <remarks>
/// Used by <c>WithBearerToken(...)</c> so a token produced by an earlier step can authenticate a
/// later one without the value ever being written into a log.
/// </remarks>
internal sealed class BearerTokenVariableAuthenticationProvider(VariableReference<string> token) : IApiAuthenticationProvider, IVariableBackedAuthenticationProvider
{
    /// <inheritdoc />
    public VariableReferenceGeneric TokenVariable => token;

    /// <inheritdoc />
    public Task ApplyAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        throw new InvalidOperationException($"{nameof(BearerTokenVariableAuthenticationProvider)} must be resolved against the variable store before it is applied.");
    }

    /// <inheritdoc />
    public IApiAuthenticationProvider Resolve(VariableStore store)
    {
        string resolved = token.GetRequiredValue(store, "bearer token");
        return new DelegateApiAuthenticationProvider(message => message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resolved));
    }
}

/// <summary>
/// Marks an authentication provider whose values come from the variable store.
/// </summary>
internal interface IVariableBackedAuthenticationProvider
{
    /// <summary>
    /// The variable carrying the credential value.
    /// </summary>
    VariableReferenceGeneric TokenVariable { get; }

    /// <summary>
    /// Resolves the provider against the current variable store.
    /// </summary>
    /// <param name="store">The variable store for the current run.</param>
    IApiAuthenticationProvider Resolve(VariableStore store);
}
