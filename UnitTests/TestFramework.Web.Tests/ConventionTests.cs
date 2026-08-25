using System;
using System.Linq;
using TestFramework.Core.Conventions;
using TestFramework.Web.Stub.Mappings;
using Xunit.Abstractions;

namespace TestFramework.Web.Tests;

/// <summary>
/// The family's rules, checked against this package rather than trusted to have been followed.
/// </summary>
/// <remarks>
/// These live in Core (<see cref="StepConventions"/>) and every package calls them on its own assembly: a
/// rule only Core's suite enforces is a rule only Core follows.
/// </remarks>
public class ConventionTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryStepInThisPackageClonesItself()
    {
        // A step that inherits a concrete base class's Clone() runs as that base class and silently loses
        // whatever it added. The compiler only catches the case where the base is abstract.
        ConventionReport report = StepConventions.AssertEveryStepClonesItself(typeof(StubMapping).Assembly);

        output.WriteLine(report.ToString());
        Assert.True(report.Checked > 0, "the check found no steps at all, so it proved nothing");
    }

    [Fact]
    public void FreezingCascadesThroughThisPackagesParts()
    {
        ConventionReport report = StepConventions.AssertFreezingCascades(typeof(StubMapping).Assembly);

        output.WriteLine(report.ToString());
        foreach (string skipped in report.Skipped)
        {
            output.WriteLine($"  skipped {skipped}");
        }
    }

    [Fact]
    public void ThisPackageSerialisesWithOneJsonLibrary()
    {
        // The family picked Newtonsoft.Json. Two libraries mean two sets of attributes, two notions of
        // what null means, and values that survive one round trip but not the other - and the seam shows
        // up as a bug in whichever package sits between them. Checked against the compiled assembly,
        // because a stray using is invisible in a diff.
        Assert.DoesNotContain(
            "System.Text.Json",
            typeof(StubMapping).Assembly.GetReferencedAssemblies().Select(static reference => reference.Name));
    }
}
