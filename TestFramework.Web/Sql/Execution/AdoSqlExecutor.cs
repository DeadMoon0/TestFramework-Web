using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Web.Sql.Exceptions;

namespace TestFramework.Web.Sql.Execution;

/// <summary>
/// Executes statements over <see cref="SqlConnection"/>, resolving the connection string per identifier.
/// </summary>
public sealed class AdoSqlExecutor : ISqlExecutor
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Creates an executor bound to a run's services.
    /// </summary>
    /// <param name="serviceProvider">The service provider for the current run.</param>
    public AdoSqlExecutor(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(string identifier, string statement, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
        => RunAsync(identifier, statement, parameters, cancellationToken, (connection, command) => connection.ExecuteAsync(command));

    /// <inheritdoc />
    public Task<TValue?> ScalarAsync<TValue>(string identifier, string statement, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
        => RunAsync(identifier, statement, parameters, cancellationToken, (connection, command) => connection.ExecuteScalarAsync<TValue?>(command));

    /// <inheritdoc />
    public async Task<IReadOnlyList<TRow>> QueryAsync<TRow>(string identifier, string statement, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        IEnumerable<TRow> rows = await RunAsync(
            identifier,
            statement,
            parameters,
            cancellationToken,
            (connection, command) => connection.QueryAsync<TRow>(command)).ConfigureAwait(false);

        return [.. rows];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every batch runs over one open connection, so connection state survives a <c>GO</c>: a
    /// <c>#temp</c> table created in one batch is still there in the next, and so are <c>SET</c>
    /// options and an explicit transaction.
    /// </remarks>
    public async Task<SqlScriptExecutionResult> ExecuteScriptAsync(
        string identifier,
        IReadOnlyList<string> batches,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(parameters);

        if (batches.Count == 0)
            return new SqlScriptExecutionResult(0, 0);

        SqlConfig config = SqlConfigResolver.Resolve(_serviceProvider, identifier);
        string connectionString = ResolveConnectionString(identifier);
        Stopwatch stopwatch = Stopwatch.StartNew();

        int batchNumber = 0;
        string currentBatch = batches[0];

        try
        {
            using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            int affected = 0;
            foreach (string batch in batches)
            {
                batchNumber++;
                currentBatch = batch;
                affected += await connection.ExecuteAsync(CreateCommand(batch, parameters, config, cancellationToken)).ConfigureAwait(false);
            }

            // Read before the connection closes: a transaction left open here is rolled back on close
            // without any error, which is exactly the failure that is worth naming out loud.
            int openTransactions = await connection
                .ExecuteScalarAsync<int>(CreateCommand("SELECT @@TRANCOUNT;", EmptyParameters, config, cancellationToken))
                .ConfigureAwait(false);

            return new SqlScriptExecutionResult(affected, openTransactions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException or TimeoutException)
        {
            stopwatch.Stop();
            throw SqlExecutionFailedException.Statement(
                identifier,
                SqlConnectionStringFactory.Describe(connectionString),
                currentBatch,
                parameters.Keys,
                stopwatch.Elapsed,
                exception,
                batchNumber,
                batches.Count);
        }
    }

    /// <inheritdoc />
    public string Describe(string identifier) => SqlConnectionStringFactory.Describe(ResolveConnectionString(identifier));

    private static readonly IReadOnlyDictionary<string, object?> EmptyParameters = new Dictionary<string, object?>(StringComparer.Ordinal);

    private static CommandDefinition CreateCommand(
        string statement,
        IReadOnlyDictionary<string, object?> parameters,
        SqlConfig config,
        CancellationToken cancellationToken)
        => new(
            statement,
            ToDynamicParameters(parameters),
            commandType: CommandType.Text,
            commandTimeout: config.CommandTimeout is { } timeout ? (int)timeout.TotalSeconds : null,
            cancellationToken: cancellationToken);

    private async Task<TResult> RunAsync<TResult>(
        string identifier,
        string statement,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken,
        Func<SqlConnection, CommandDefinition, Task<TResult>> execute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        ArgumentNullException.ThrowIfNull(parameters);

        SqlConfig config = SqlConfigResolver.Resolve(_serviceProvider, identifier);
        string connectionString = ResolveConnectionString(identifier);
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return await execute(connection, CreateCommand(statement, parameters, config, cancellationToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException or TimeoutException)
        {
            stopwatch.Stop();
            throw SqlExecutionFailedException.Statement(
                identifier,
                SqlConnectionStringFactory.Describe(connectionString),
                statement,
                parameters.Keys,
                stopwatch.Elapsed,
                exception);
        }
    }

    private string ResolveConnectionString(string identifier)
    {
        SqlConfig config = SqlConfigResolver.Resolve(_serviceProvider, identifier);
        SqlCredentials? credentials = _serviceProvider.GetService<ISqlCredentialProvider>()?.GetCredentials(identifier);
        return SqlConnectionStringFactory.Create(identifier, config, credentials);
    }

    private static DynamicParameters ToDynamicParameters(IReadOnlyDictionary<string, object?> parameters)
    {
        DynamicParameters dynamicParameters = new();
        foreach ((string name, object? value) in parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            dynamicParameters.Add(name, value);

        return dynamicParameters;
    }
}
