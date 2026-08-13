using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Configuration;
using TestFramework.Web.Extensions;
using TestFramework.Web.Sql;
using TestFramework.Web.Sql.Configuration;
using TestFramework.Web.Sql.Exceptions;
using Xunit;

namespace TestFramework.Web.Tests.Sql;

public class SqlConfigurationTests
{
    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    [Fact]
    public void LoadSqlConfig_ReadsStructuredParts()
    {
        IConfiguration configuration = BuildConfiguration(
            ("Sql:main:Server", "localhost,1433"),
            ("Sql:main:Database", "SampleDb"),
            ("Sql:main:IntegratedSecurity", "true"),
            ("Sql:main:TrustServerCertificate", "true"),
            ("Sql:main:CommandTimeout", "00:00:45"));

        SqlConfig config = new DefaultSqlConfigProvider().LoadSqlConfig(configuration, "main");

        Assert.Equal("localhost,1433", config.Server);
        Assert.Equal("SampleDb", config.Database);
        Assert.True(config.IntegratedSecurity);
        Assert.True(config.TrustServerCertificate);
        Assert.Equal(TimeSpan.FromSeconds(45), config.CommandTimeout);
    }

    [Fact]
    public void LoadSqlConfig_Throws_WhenNeitherConnectionStringNorServerIsConfigured()
    {
        IConfiguration configuration = BuildConfiguration(("Sql:main:Database", "SampleDb"));

        SqlConfigurationValidationException exception = Assert.Throws<SqlConfigurationValidationException>(
            () => new DefaultSqlConfigProvider().LoadSqlConfig(configuration, "main"));

        Assert.Contains("Sql:main:Server", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sql:main:CommandTimeout", "soon")]
    [InlineData("Sql:main:IntegratedSecurity", "perhaps")]
    public void LoadSqlConfig_Throws_WithTheKeyAndValueNamed_WhenAValueCannotBeParsed(string key, string value)
    {
        IConfiguration configuration = BuildConfiguration(("Sql:main:Server", "localhost"), ("Sql:main:Database", "SampleDb"), (key, value));

        SqlConfigurationValidationException exception = Assert.Throws<SqlConfigurationValidationException>(
            () => new DefaultSqlConfigProvider().LoadSqlConfig(configuration, "main"));

        Assert.Contains(value, exception.Message, StringComparison.Ordinal);
        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadWebSqlConfigs_RegistersTheStore_EvenWithoutASqlSection()
    {
        ServiceCollection services = new();
        services.LoadWebSqlConfigs(BuildConfiguration());

        WebConfigStore<SqlConfig> store = services.BuildServiceProvider().GetRequiredService<WebConfigStore<SqlConfig>>();

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void Resolve_Throws_WithKnownIdentifiersListed_WhenIdentifierIsUnknown()
    {
        ServiceCollection services = new();
        services.LoadWebSqlConfigs(BuildConfiguration(("Sql:main:Server", "localhost"), ("Sql:main:Database", "SampleDb")));

        SqlConfigurationValidationException exception = Assert.Throws<SqlConfigurationValidationException>(
            () => SqlConfigResolver.Resolve(services.BuildServiceProvider(), "other"));

        Assert.Contains("other", exception.Message, StringComparison.Ordinal);
        Assert.Contains("main", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionString_IsComposedFromTheStructuredParts()
    {
        SqlConfig config = new() { Server = "localhost,1433", Database = "SampleDb", IntegratedSecurity = true, TrustServerCertificate = true };

        string connectionString = SqlConnectionStringFactory.Create("main", config);

        Assert.Contains("Data Source=localhost,1433", connectionString, StringComparison.Ordinal);
        Assert.Contains("Initial Catalog=SampleDb", connectionString, StringComparison.Ordinal);
        Assert.Contains("Integrated Security=True", connectionString, StringComparison.Ordinal);
        Assert.Contains("Trust Server Certificate=True", connectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeCredentials_ReplaceIntegratedSecurityWithASqlLogin()
    {
        // This is the developer-machine to container switch: one settings file, two identities.
        SqlConfig config = new() { Server = "localhost", Database = "SampleDb", IntegratedSecurity = true };

        string connectionString = SqlConnectionStringFactory.Create("main", config, SqlCredentials.Login("sa", "secret"));

        Assert.Contains("User ID=sa", connectionString, StringComparison.Ordinal);
        Assert.DoesNotContain("Integrated Security=True", connectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeCredentials_CanSwitchALoginBackToIntegratedSecurity()
    {
        SqlConfig config = new() { Server = "localhost", Database = "SampleDb", UserName = "sa", Password = "secret" };

        string connectionString = SqlConnectionStringFactory.Create("main", config, SqlCredentials.Integrated);

        Assert.Contains("Integrated Security=True", connectionString, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", connectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_NamesTheServerAndDatabaseButNeverTheCredentials()
    {
        SqlConfig config = new() { Server = "localhost", Database = "SampleDb", UserName = "sa", Password = "top-secret" };
        string connectionString = SqlConnectionStringFactory.Create("main", config);

        string described = SqlConnectionStringFactory.Describe(connectionString);

        Assert.Equal("localhost/SampleDb", described);
        Assert.DoesNotContain("top-secret", described, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedConnectionString_FailsWithoutEchoingTheValue()
    {
        // The value routinely carries a password, so it must not appear in the message.
        SqlConfig config = new() { ConnectionString = "this is not=a=valid;;;connection string" };

        SqlConfigurationValidationException exception = Assert.Throws<SqlConfigurationValidationException>(
            () => SqlConnectionStringFactory.Create("main", config));

        Assert.Contains("ConnectionString", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("valid;;;connection", exception.Message, StringComparison.Ordinal);
    }
}
