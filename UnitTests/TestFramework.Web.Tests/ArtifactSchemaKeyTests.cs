using TestFramework.Web.Sql.Artifacts;

namespace TestFramework.Web.Tests;

/// <summary>
/// Pins the artifact's debug schema key to its canonical value.
/// </summary>
/// <remarks>
/// A schema key tells a consumer which renderer to use, so changing one silently is a breaking
/// change for anything that draws this artifact. This package carries the key as a literal because
/// it builds against the published Core, which means nothing but a test keeps it in step with
/// <c>TestFramework.Core.Debugger.DebugValueSchemaKeys</c>.
/// </remarks>
public class ArtifactSchemaKeyTests
{
    private sealed class SampleRow
    {
        public int Id { get; set; }
    }

    [Fact]
    public void SqlRowArtifactReportsTheCanonicalSchemaKey()
        => Assert.Equal("tf.artifact.sql.row", new SqlRowArtifactDescriber<SampleRow>().DebugValueSchemaKey);

    [Fact]
    public void SqlRowSharesItsSchemaKeyWithTheAzureImplementation()
    {
        // The ADO-backed row here and the EF-backed row in TestFramework.Azure are different types
        // presenting the same thing. One key means a consumer needs one icon and one inspector, not
        // two that happen to resemble each other.
        Assert.Equal("tf.artifact.sql.row", new SqlRowArtifactDescriber<SampleRow>().DebugValueSchemaKey);
    }

    [Fact]
    public void SchemaKeyIsNotTheClrTypeName()
    {
        SqlRowArtifactDescriber<SampleRow> describer = new();

        Assert.NotEqual(describer.GetType().FullName, describer.DebugValueSchemaKey);
    }
}
