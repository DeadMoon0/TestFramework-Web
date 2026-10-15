using System;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Variables;
using Xunit;

namespace TestFramework.Web.Tests;

/// <summary>
/// A value verb takes the value; its ...From twin reads a variable; a variable passed as a value is refused when the
/// timeline is built.
/// </summary>
public class ValueVerbTests
{
    [Fact]
    public void AVariablePassedAsAStatementParameter_IsRefusedNamingWithParameterFrom()
    {
        FrameworkConfigurationException refused = Assert.Throws<FrameworkConfigurationException>(
            () => WebExt.Sql.Execute("main", "DELETE FROM Orders WHERE Id = @id").WithParameter("id", Var.Ref<int>("orderId")));

        Assert.Contains("WithParameterFrom", refused.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AVariablePassedAsAFinderParameter_IsRefused()
        => Assert.Throws<FrameworkConfigurationException>(
            () => WebExt.ArtifactFinder.Sql.Where<Order>("main", "Id = @id").WithParameter("id", Var.RefImmutable<int>("orderId")));

    [Fact]
    public void AVariablePassedAsAJsonBody_IsRefusedNamingWithJsonBodyFrom()
    {
        FrameworkConfigurationException refused = Assert.Throws<FrameworkConfigurationException>(
            () => WebExt.Api.Http("orders").Post("api/orders").WithJsonBody(Var.Ref<Order>("order")));

        Assert.Contains("WithJsonBodyFrom", refused.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AConstant_IsAValue()
    {
        // A constant is a value, so the value verb takes it; only a variable read at run time is refused.
        WebExt.Sql.Execute("main", "DELETE FROM Orders WHERE Id = @id").WithParameter("id", Var.Const(7));
    }

    public sealed class Order
    {
        public int Id { get; set; }
    }
}
