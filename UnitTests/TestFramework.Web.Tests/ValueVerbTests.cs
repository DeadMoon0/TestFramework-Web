using System;
using System.Linq;
using System.Reflection;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Variables;
using TestFramework.Web.Builder.Actions;
using TestFramework.Web.Sql.Artifacts;
using TestFramework.Web.Sql.Steps;
using Xunit;

namespace TestFramework.Web.Tests;

/// <summary>
/// A value verb takes the value; its ...From twin reads a variable; a variable passed as a value does not compile.
/// </summary>
public class ValueVerbTests
{
    [Theory]
    [InlineData(typeof(SqlRowWhereFinder<>), "WithParameter")]
    [InlineData(typeof(SqlExecuteStep), "WithParameter")]
    [InlineData(typeof(SqlScalarStep<>), "WithParameter")]
    [InlineData(typeof(SqlScriptStep), "WithParameter")]
    [InlineData(typeof(IWithBodyAction), "WithJsonBody")]
    public void EveryValueVerb_RefusesAShippedReferenceTypeAtCompileTime(Type owner, string verb)
    {
        Type[] guarded = [.. owner.GetMethods()
            .Where(method => method.Name == verb && method.GetCustomAttribute<ObsoleteAttribute>() is { IsError: true })
            .Select(method => method.GetParameters()[^1].ParameterType.GetGenericTypeDefinition())];

        Assert.Equal(
            new[] { typeof(ImmutableVariable<,>), typeof(ResolvableVariable<>), typeof(VariableReference<>) }.Select(type => type.Name),
            guarded.Select(type => type.Name).Order(StringComparer.Ordinal));
        Assert.Contains(owner.GetMethods(), method => method.Name == verb + "From");
    }

    [Fact]
    public void AVariableThatArrivesAsAPlainValue_IsRefused()
    {
        // The compiler catches a reference passed directly; this is one hidden behind a caller's own generic helper.
        static SqlExecuteStep BindThrough<T>(SqlExecuteStep step, T value) => step.WithParameter("id", value);

        FrameworkConfigurationException refused = Assert.Throws<FrameworkConfigurationException>(
            () => BindThrough(WebExt.Sql.Execute("main", "DELETE FROM Orders WHERE Id = @id"), Var.Ref<int>("orderId")));

        Assert.Contains("WithParameterFrom", refused.ToString(), StringComparison.Ordinal);
    }
}
