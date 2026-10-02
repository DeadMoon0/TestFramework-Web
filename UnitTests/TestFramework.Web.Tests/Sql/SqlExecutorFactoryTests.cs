using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Config;
using TestFramework.Config.Builder.InstanceBuilder;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;
using TestFramework.Core.Timelines;
using TestFramework.Web.Extensions;
using TestFramework.Web.Sql;
using TestFramework.Web.Sql.Execution;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

/// <summary>
/// Pins that a custom executor sees the run it executes for.
/// </summary>
/// <remarks>
/// The damage it prevents is a seam that cannot do its one job. An executor registered as a service was a
/// singleton handed nothing, so it could never resolve where a database is - and the one place a container's
/// connection string exists is the run.
/// </remarks>
public sealed class SqlExecutorFactoryTests
{
    [Fact]
    public async Task TheFactoryIsHandedTheStepsRun_SoItsExecutorCanReadTheRunsConfiguration()
    {
        RunAwareFactory factory = new();

        TimelineRun run = await Timeline.Create()
            .Trigger(WebExt.Sql.Execute("main", "UPDATE Orders SET Quantity = 1")).Name("update")
            .Build()
            .SetupRun(Config(builder => builder.UseSqlExecutor(factory)))
            .RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal("SampleDb", factory.DatabaseSeen);
        Assert.Single(factory.Executor.Calls);
    }

    [Fact]
    public async Task AnExecutorRegisteredAsAServiceIsRefusedNamingTheFactory()
    {
        // Ignoring it would silently swap the author's executor for the built-in one.
        TimelineRun run = await Timeline.Create()
            .Trigger(WebExt.Sql.Execute("main", "UPDATE Orders SET Quantity = 1")).Name("update")
            .Build()
            .SetupRun(Config(builder => builder.AddService(services => services.AddSingleton<ISqlExecutor>(new RecordingSqlExecutor()))))
            .RunAsync();

        TimelineRunFailedException failure = Assert.Throws<TimelineRunFailedException>(run.EnsureRanToCompletion);
        Assert.Contains("ISqlExecutorFactory", failure.Message, StringComparison.Ordinal);
    }

    private static ConfigInstance Config(Func<IConfigInstanceBuilder, IConfigInstanceBuilder> configure)
        => configure(ConfigInstance.Create()
            .OverrideConfig(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Sql:main:Server"] = "localhost",
                ["Sql:main:Database"] = "SampleDb",
                ["Sql:main:IntegratedSecurity"] = "true",
            })
            .LoadWebConfig())
            .Build();

    private sealed class RunAwareFactory : ISqlExecutorFactory
    {
        public RecordingSqlExecutor Executor { get; } = new();

        public string? DatabaseSeen { get; private set; }

        public ISqlExecutor Create(RunContext context)
        {
            DatabaseSeen = context.Configured<SqlConfig>("main").Database;
            return Executor;
        }
    }
}
