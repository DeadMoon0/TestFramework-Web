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

    [Fact]
    public void ThisPackageKeepsItsInternalsToItself()
    {
        // Every package is a stranger to every other. A grant to another package is a private handshake:
        // two packages understand each other and a third cannot join, so what the favoured one may do stops
        // being what any of them may do - and the grant hides the fact that a surface is missing.
        ConventionReport report = StepConventions.AssertNoPackageSeesAnothersInternals(typeof(StubMapping).Assembly);

        output.WriteLine(report.ToString());
    }

    [Fact]
    public void ThisPackagesPublicSurfaceCanGrow()
    {
        // Each package is compiled on its own. A knob added as one more optional parameter, or one more member
        // on a positional record, compiles everywhere and then throws MissingMethodException in every package
        // built against the old signature.
        ConventionReport report = StepConventions.AssertPublicSurfaceCanGrow(typeof(StubMapping).Assembly);

        output.WriteLine(report.ToString());
    }

    [Fact]
    public void EveryExceptionTypeThisPackageDeclaresIsAFrameworkException()
    {
        // One base, so a failure carries its recovery steps and reads the same way in a run's failure detail as
        // every other package's.
        string[] strays = [.. typeof(StubMapping).Assembly.GetTypes()
            .Where(type => type.IsPublic && typeof(System.Exception).IsAssignableFrom(type))
            .Where(type => !typeof(TestFramework.Core.Exceptions.TimelineFrameworkException).IsAssignableFrom(type))
            .Select(type => type.FullName!)];

        Assert.Empty(strays);
    }
}
