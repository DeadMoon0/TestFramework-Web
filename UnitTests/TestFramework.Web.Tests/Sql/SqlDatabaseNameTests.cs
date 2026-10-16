using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Steps;
using TestFramework.Web.Extensions;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// Pins that the run knows the database a connection string names.
/// </summary>
/// <remarks>
/// The damage it prevents is a probe refusing a configuration that names its database. An entry written as one full
/// connection string published no database name, so <c>IsLive(..., SqlAlivenessLevel.Database)</c> said the run held
/// none to verify against - while the connection string said <c>Database=...</c> plainly.
/// </remarks>
public sealed class SqlDatabaseNameTests
{
    [Fact]
    public void AConnectionStringsDatabaseIsTheRunsDatabaseName()
    {
        Assert.Equal("Orders", DatabaseNameOf(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Sql:main:ConnectionString"] = "Server=db;Database=Orders;Integrated Security=true",
        }));
    }

    [Fact]
    public void AnExplicitDatabaseStillWins()
    {
        Assert.Equal("SampleDb", DatabaseNameOf(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Sql:main:Server"] = "db",
            ["Sql:main:Database"] = "SampleDb",
            ["Sql:main:IntegratedSecurity"] = "true",
        }));
    }

    private static string? DatabaseNameOf(Dictionary<string, string?> settings)
    {
        ConfigInstance config = ConfigInstance.Create().OverrideConfig(settings).LoadWebConfig().Build();
        using ServiceProvider services = config.BuildServiceProvider();
        RunContext context = RunContext.Detached(services);

        return context.Values.TryGet(
            ValueRef.For(WebResourceKinds.Sql, "main", WebResourceKinds.DatabaseNameValue),
            ResourceVantage.Host,
            out string? database)
            ? database
            : null;
    }
}
