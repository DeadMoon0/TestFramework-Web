using TestFramework.Web.Site.Configuration;
using TestFramework.Web.Sql.Configuration;
using TestFramework.Web.Stub.Configuration;

namespace TestFramework.Web.Configuration;

/// <summary>
/// The readers the Web package uses for its four configuration sections, one per section.
/// </summary>
/// <remarks>
/// Every reader is optional; a section whose reader is left unset is read by its default provider. One record
/// rather than a parameter per section, so the package keeps a single call that switches it on.
/// </remarks>
public sealed record WebConfigProviders
{
    /// <summary>Reads the <c>Api</c> section. <see cref="DefaultApiConfigProvider"/> when unset.</summary>
    public IApiConfigProvider? Api { get; init; }

    /// <summary>Reads the <c>Sql</c> section. <see cref="DefaultSqlConfigProvider"/> when unset.</summary>
    public ISqlConfigProvider? Sql { get; init; }

    /// <summary>Reads the <c>Stub</c> section. <see cref="DefaultStubConfigProvider"/> when unset.</summary>
    public IStubConfigProvider? Stub { get; init; }

    /// <summary>Reads the <c>Site</c> section. <see cref="DefaultSiteConfigProvider"/> when unset.</summary>
    public ISiteConfigProvider? Site { get; init; }
}
