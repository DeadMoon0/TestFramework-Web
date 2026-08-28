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

    public override async Task<ApiIsLiveResult?> Execute(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context.Services);
        ArgumentNullException.ThrowIfNull(context.Logger);

        ApiAlivenessLevel level = alivenessLevel.GetValue(context.Variables);
        ApiConfig config = context.Configured<ApiConfig>(identifier);
        ApiTriggerConfig triggerConfig = context.Services.GetService<ApiTriggerConfig>() ?? new ApiTriggerConfig();
        Uri probeUri = BuildProbeUri(config, level);
        IHttpSender sender = context.Services.GetWebComponentFactory().CreateSender(identifier, config);

        context.Logger.LogInformation("API IsLive '{0}' probing '{1}' at level {2}.", identifier, probeUri, level);

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
                context.Logger,
                context.Deadline).ConfigureAwait(false);

            stopwatch.Stop();

            EnsureProbeSucceeded(level, response.StatusCode, probeUri);
            context.Logger.LogInformation("API IsLive '{0}' returned {1} in {2}.", identifier, response.StatusCode, stopwatch.Elapsed);

            return new ApiIsLiveResult(identifier, level, probeUri, true, response.StatusCode, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (context.Deadline.Token.IsCancellationRequested)
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
        StepDeadline deadline)
    {
        // A freshly started local host answers 404 or 503 until its routes are live. Waiting that
        // out is this step's whole purpose, so it happens here rather than on every later call.
        bool retryWarmup = IsLocalDevelopmentHost(probeUri)
            && level != ApiAlivenessLevel.Reachable
            && triggerConfig.LocalWarmupRetryDuration > TimeSpan.Zero;

        if (retryWarmup && triggerConfig.LocalWarmupRetryDelay <= TimeSpan.Zero)
        {
            throw new TestFramework.Core.Exceptions.FrameworkConfigurationException(
                $"{nameof(ApiTriggerConfig)}.{nameof(ApiTriggerConfig.LocalWarmupRetryDelay)} is {triggerConfig.LocalWarmupRetryDelay}, which would poll without pause.",
                ["Register an ApiTriggerConfig with a positive LocalWarmupRetryDelay, or leave the default in place."]);
        }

        // The warmup is this step's own budget, measured the way the step's deadline is - monotonic
        // elapsed time, never the wall clock, whose ~15 ms ticks can disagree with a timer armed from the
        // same figure. And it never outlives the step: when what remains of the deadline cannot fit
        // another wait, the loop returns its last answer instead of dying mid-sleep as a bare
        // cancellation - so the probe's own verdict, with the status it last saw and the ways out of it,
        // is what the reader gets.
        Stopwatch warmup = Stopwatch.StartNew();
        bool announced = false;

        while (true)
        {
            using HttpRequestMessage message = new(HttpMethod.Get, probeUri);
            if (level == ApiAlivenessLevel.Authenticated)
                await new ConfiguredApiAuthenticationProvider(identifier, config).ApplyAsync(message, deadline.Token).ConfigureAwait(false);

            HttpResponseMessage response = await sender.SendAsync(message, deadline.Token).ConfigureAwait(false);
            bool warmupStatus = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.ServiceUnavailable;

            if (!retryWarmup || !warmupStatus || warmup.Elapsed >= triggerConfig.LocalWarmupRetryDuration)
                return response;

            if (!deadline.IsUnbounded && deadline.Remaining <= triggerConfig.LocalWarmupRetryDelay)
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
            await Task.Delay(triggerConfig.LocalWarmupRetryDelay, deadline.Token).ConfigureAwait(false);
        }
    }

    private static bool IsLocalDevelopmentHost(Uri uri)
        => uri.IsLoopback
        || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase);

    private Uri BuildProbeUri(ApiConfig config, ApiAlivenessLevel level)
        => HttpRequestSpec.Join(identifier, config.BaseUrl, level == ApiAlivenessLevel.Reachable ? string.Empty : config.HealthPath);

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
