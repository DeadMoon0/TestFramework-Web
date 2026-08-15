using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Core.Artifacts;
using TestFramework.Core.Environment;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.Web.Auth;
using TestFramework.Web.Configuration;
using TestFramework.Web.Exceptions;
using TestFramework.Web.Http;
using TestFramework.Web.Identifier;
using TestFramework.Web.Runtime;

namespace TestFramework.Web.Trigger.IsLive;

/// <summary>
/// Probes whether a configured API is answering.
/// </summary>
/// <remarks>
/// Against a local host this is the step that waits: a <c>404</c> or <c>503</c> from a loopback or
/// <c>host.docker.internal</c> authority is retried for
/// <see cref="ApiTriggerConfig.LocalWarmupRetryDuration"/> while the route table comes up. Against
/// any other authority the same status fails immediately — a remote 404 is a real answer.
/// </remarks>
internal sealed class ApiIsLiveTrigger(ApiIdentifier identifier, VariableReference<ApiAlivenessLevel> alivenessLevel)
    : Step<ApiIsLiveResult>, IHasEnvironmentRequirements
{
    public override string Name => "API IsLive Trigger";

    public override string Description => $"Checks whether the API '{identifier}' is answering";

    public override bool DoesReturn => true;

    public override StepExecutionPhase Phase => StepExecutionPhase.Observe;

    public override Step<ApiIsLiveResult> Clone() => new ApiIsLiveTrigger(identifier, alivenessLevel).WithClonedOptions(this);

    public override StepInstance<Step<ApiIsLiveResult>, ApiIsLiveResult> GetInstance() =>
        new StepInstance<Step<ApiIsLiveResult>, ApiIsLiveResult>(this);

    public override void DeclareIO(StepIOContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        if (alivenessLevel.HasIdentifier && alivenessLevel.Identifier is { } variableIdentifier)
            contract.Inputs.Add(new StepIOEntry(variableIdentifier.Identifier, StepIOKind.Variable, false, typeof(ApiAlivenessLevel)));
    }

    public IReadOnlyCollection<EnvironmentRequirement> GetEnvironmentRequirements(VariableStore variableStore)
        => [new EnvironmentRequirement(WebEnvironmentResourceKinds.RestApi, identifier)];

    public override async Task<ApiIsLiveResult?> Execute(
        IServiceProvider serviceProvider,
        VariableStore variableStore,
        ArtifactStore artifactStore,
        ScopedLogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        ApiAlivenessLevel level = alivenessLevel.GetValue(variableStore);
        ApiConfig config = ApiConfigResolver.Resolve(serviceProvider, identifier);
        ApiTriggerConfig triggerConfig = serviceProvider.GetService<ApiTriggerConfig>() ?? new ApiTriggerConfig();
        Uri probeUri = BuildProbeUri(config, level);
        IHttpSender sender = serviceProvider.GetWebComponentFactory().CreateSender(identifier, config);

        logger.LogInformation("API IsLive '{0}' probing '{1}' at level {2}.", identifier, probeUri, level);

        Stopwatch stopwatch = Stopwatch.StartNew();
        HttpResponseMessage? response = null;

        try
        {
            response = await ProbeWithLocalWarmupRetryAsync(
                sender,
                config,
                level,
                probeUri,
                triggerConfig,
                logger,
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            EnsureProbeSucceeded(level, response.StatusCode, probeUri);
            logger.LogInformation("API IsLive '{0}' returned {1} in {2}.", identifier, response.StatusCode, stopwatch.Elapsed);

            return new ApiIsLiveResult(identifier, level, probeUri, true, response.StatusCode, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            stopwatch.Stop();
            throw ApiRequestFailedException.Transport(identifier, HttpMethod.Get, probeUri, stopwatch.Elapsed, exception);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<HttpResponseMessage> ProbeWithLocalWarmupRetryAsync(
        IHttpSender sender,
        ApiConfig config,
        ApiAlivenessLevel level,
        Uri probeUri,
        ApiTriggerConfig triggerConfig,
        ScopedLogger logger,
        CancellationToken cancellationToken)
    {
        // A freshly started local host answers 404 or 503 until its routes are live. Waiting that
        // out is this step's whole purpose, so it happens here rather than on every later call.
        bool retryWarmup = IsLocalDevelopmentHost(probeUri)
            && level != ApiAlivenessLevel.Reachable
            && triggerConfig.LocalWarmupRetryDuration > TimeSpan.Zero;

        DateTime deadline = DateTime.UtcNow.Add(triggerConfig.LocalWarmupRetryDuration);
        bool announced = false;

        while (true)
        {
            using HttpRequestMessage message = new(HttpMethod.Get, probeUri);
            if (level == ApiAlivenessLevel.Authenticated)
                await new ConfiguredApiAuthenticationProvider(identifier, config).ApplyAsync(message, cancellationToken).ConfigureAwait(false);

            HttpResponseMessage response = await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
            bool warmupStatus = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.ServiceUnavailable;

            if (!retryWarmup || !warmupStatus || DateTime.UtcNow >= deadline)
                return response;

            if (!announced)
            {
                // One line for the whole wait: a per-attempt log would bury the run in noise.
                logger.LogInformation(
                    "API IsLive '{0}' got {1} from local host '{2}'. Waiting up to {3} for it to warm up.",
                    identifier,
                    response.StatusCode,
                    probeUri.Authority,
                    triggerConfig.LocalWarmupRetryDuration);

                announced = true;
            }

            response.Dispose();
            await Task.Delay(triggerConfig.LocalWarmupRetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsLocalDevelopmentHost(Uri uri)
        => uri.IsLoopback
        || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase);

    private Uri BuildProbeUri(ApiConfig config, ApiAlivenessLevel level)
    {
        if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out Uri? baseUri))
            throw ApiConfigurationValidationException.InvalidValue(identifier, nameof(ApiConfig.BaseUrl), $"'{config.BaseUrl}' is not an absolute URL");

        if (level == ApiAlivenessLevel.Reachable)
            return baseUri;

        string healthPath = config.HealthPath.TrimStart('/');
        string separator = baseUri.AbsoluteUri.EndsWith('/') ? string.Empty : "/";
        return new Uri(baseUri.AbsoluteUri + separator + healthPath, UriKind.Absolute);
    }

    private void EnsureProbeSucceeded(ApiAlivenessLevel level, HttpStatusCode statusCode, Uri probeUri)
    {
        // Reachable only proves the host answered: any status code, including 404 or 401, counts.
        if (level == ApiAlivenessLevel.Reachable)
            return;

        bool success = (int)statusCode is >= 200 and <= 299;
        if (success)
            return;

        List<string> recovery = [];
        if (statusCode is HttpStatusCode.NotFound)
            recovery.Add($"Set 'Api:{identifier}:HealthPath' to a path this API actually serves, or probe at Reachable level instead.");

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            recovery.Add(level == ApiAlivenessLevel.Authenticated
                ? $"Check 'Api:{identifier}:Auth' and its credential values."
                : $"The health path requires authentication; probe at Authenticated level or expose an anonymous health endpoint.");
        }

        throw ApiLivenessProbeException.Failed(identifier, level, probeUri, statusCode, recovery);
    }
}
