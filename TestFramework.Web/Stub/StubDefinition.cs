using System.Collections.Generic;
using TestFramework.Web.Stub.Mappings;

namespace TestFramework.Web.Stub;

/// <summary>
/// Declares a stubbed dependency: what it answers, and under which identifier.
/// </summary>
/// <remarks>
/// A definition says nothing about where the stub runs. The environment the run is set up with
/// decides that, so the same definition serves a container-hosted stub and any other host without
/// being touched.
/// </remarks>
/// <example>
/// <code>
/// internal sealed class PaymentsStubDefinition : StubDefinition
/// {
///     public override StubIdentifier Identifier =&gt; "payments";
///
///     protected override void Configure(StubMappingBuilder builder) =&gt; builder
///         .OnPost("/api/charges")
///             .RespondJson(HttpStatusCode.Created, new { status = "captured" });
/// }
/// </code>
/// </example>
public abstract class StubDefinition
{
    /// <summary>
    /// The stub identifier timelines and application settings use to reach this stub.
    /// </summary>
    public abstract StubIdentifier Identifier { get; }

    /// <summary>
    /// Declares the requests this stub recognises and the answers it gives.
    /// </summary>
    /// <param name="builder">The builder collecting the declaration.</param>
    protected abstract void Configure(StubMappingBuilder builder);

    /// <summary>
    /// Builds the declared mappings, in declaration order.
    /// </summary>
    public IReadOnlyList<StubMapping> Build()
    {
        StubMappingBuilder builder = new();
        Configure(builder);
        return builder.Build();
    }
}
