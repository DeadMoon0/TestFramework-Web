using System;
using TestFramework.Web.Sql;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// Pins how a configured SQL timeout reaches the SQL client.
/// </summary>
public sealed class SqlTimeoutsTests
{
    [Fact]
    public void NoCommandTimeoutGivesTheCommandNoLimitOfItsOwn()
        => Assert.Equal(0, SqlTimeouts.CommandSeconds(null));

    [Fact]
    public void ASubSecondTimeoutRoundsUpInsteadOfBecomingForever()
    {
        // 0 means "wait forever" to the SQL client - the opposite of what half a second asks for.
        Assert.Equal(1, SqlTimeouts.CommandSeconds(TimeSpan.FromMilliseconds(500)));
        Assert.Equal(1, SqlTimeouts.WholeSeconds(TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public void AFractionalTimeoutRoundsUp()
        => Assert.Equal(3, SqlTimeouts.CommandSeconds(TimeSpan.FromSeconds(2.2)));
}
