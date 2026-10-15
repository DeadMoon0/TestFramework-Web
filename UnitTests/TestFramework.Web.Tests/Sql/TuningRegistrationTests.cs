using System;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config;
using TestFramework.Core.Exceptions;
using TestFramework.Web.Extensions;
using TestFramework.Web.Sql;
using TestFramework.Web.Sql.Model;
using TestFramework.Web.Sql.Steps;
using TestFramework.Web.Trigger;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// Two projects configuring one run: each tuning call adds to the run instead of replacing what another made.
/// </summary>
public class TuningRegistrationTests
{
    public sealed class Order
    {
        public string OrderCode { get; set; } = "";
    }

    public sealed class Customer
    {
        public string CustomerCode { get; set; } = "";
    }

    [Fact]
    public void ModelsMappedByTwoRegistrations_AreAllKnownToTheRun()
    {
        using ServiceProvider provider = ConfigInstance.Create()
            .AddWebSqlModels(models => models.For<Order>().Table("Orders").Key(x => x.OrderCode))
            .AddWebSqlModels(models => models.For<Customer>().Table("Customers").Key(x => x.CustomerCode))
            .BuildServiceProvider();

        SqlModelRegistry registry = SqlConfigResolver.ResolveModelRegistry(provider);

        Assert.Equal("Orders", registry.Resolve<Order>().Table);
        Assert.Equal("Customers", registry.Resolve<Customer>().Table);
    }

    [Fact]
    public void AModelMappedByTwoRegistrations_IsRefusedByName()
    {
        using ServiceProvider provider = ConfigInstance.Create()
            .AddWebSqlModels(models => models.For<Order>().Table("Orders").Key(x => x.OrderCode))
            .AddWebSqlModels(models => models.For<Order>().Table("OrderArchive").Key(x => x.OrderCode))
            .BuildServiceProvider();

        FrameworkConfigurationException refused = Assert.Throws<FrameworkConfigurationException>(() => SqlConfigResolver.ResolveModelRegistry(provider));

        Assert.Contains("'Order'", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoSqlStepTunings_BothApply()
    {
        using ServiceProvider provider = ConfigInstance.Create()
            .ConfigureSqlSteps(config => config with { LogStatements = false })
            .ConfigureSqlSteps(config => config with { LogParameterValues = true })
            .BuildServiceProvider();

        SqlStepConfig config = provider.GetRequiredService<SqlStepConfig>();

        Assert.False(config.LogStatements);
        Assert.True(config.LogParameterValues);
    }

    [Fact]
    public void LaterApiTriggerTunings_SeeWhatEarlierOnesSet()
    {
        using ServiceProvider provider = ConfigInstance.Create()
            .ConfigureApiTrigger(config => config with { LogRequestHeaders = true })
            .ConfigureApiTrigger(config => config with { LogRequestHeaders = !config.LogRequestHeaders })
            .BuildServiceProvider();

        Assert.False(provider.GetRequiredService<ApiTriggerConfig>().LogRequestHeaders);
    }

    [Fact]
    public void ASecondSqlExecutorFactory_IsRefused()
    {
        RecordingSqlExecutor executor = new();

        FrameworkConfigurationException refused = Assert.Throws<FrameworkConfigurationException>(() => ConfigInstance.Create()
            .UseSqlExecutor(new FixedSqlExecutorFactory(executor))
            .UseSqlExecutor(new FixedSqlExecutorFactory(executor))
            .BuildServiceProvider());

        Assert.Contains("UseSqlExecutor", refused.Message, StringComparison.Ordinal);
    }
}
