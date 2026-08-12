using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using TestFramework.Config;
using TestFramework.Config.Builder.InstanceBuilder;
using TestFramework.Web.Extensions;
using TestFramework.Web.SampleApi;
using Xunit;

namespace TestFramework.Web.Tests;

/// <summary>
/// Starts the sample API once per test collection on an ephemeral loopback port.
/// </summary>
/// <remarks>
/// The tests deliberately go over a real socket rather than an in-memory test host: the point of
/// this suite is to exercise the production sender, including timeouts and warmup retries.
/// </remarks>
public sealed class SampleApiFixture : IAsyncLifetime
{
    private WebApplication? _app;

    /// <summary>
    /// Absolute base URL of the running sample API, including its assigned port.
    /// </summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _app = SampleApiHost.Create();
        await _app.StartAsync();
        BaseUrl = SampleApiHost.GetBaseUrl(_app);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    /// <summary>
    /// Builds a run configuration that points an API identifier at the running sample API.
    /// </summary>
    /// <param name="identifier">The API identifier to register.</param>
    /// <param name="configure">Optional extra configuration values, relative to the identifier.</param>
    /// <returns>The configuration instance for the run.</returns>
    public ConfigInstance CreateConfig(string identifier = "sample", Action<Dictionary<string, string?>>? configure = null)
    {
        Dictionary<string, string?> values = new(StringComparer.Ordinal)
        {
            [$"Api:{identifier}:BaseUrl"] = BaseUrl,
            [$"Api:{identifier}:HealthPath"] = "/health",
        };

        configure?.Invoke(values);

        IConfigInstanceBuilder builder = ConfigInstance.Create()
            .OverrideConfig(values)
            .LoadWebConfig();

        return builder.Build();
    }
}

/// <summary>
/// Collection definition that shares one sample API across the integration tests.
/// </summary>
[CollectionDefinition(CollectionName)]
public sealed class SampleApiCollection : ICollectionFixture<SampleApiFixture>
{
    /// <summary>
    /// Name of the xUnit collection that shares the sample API.
    /// </summary>
    public const string CollectionName = "sample-api";
}
