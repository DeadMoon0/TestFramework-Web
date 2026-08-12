using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
        Uri probeUri = BuildProbeUri(config, level);
        IHttpSender sender = serviceProvider.GetWebComponentFactory().CreateSender(identifier, config);

        logger.LogInformation("API IsLive '{0}' probing '{1}' at level {2}.", identifier, probeUri, level);

        Stopwatch stopwatch = Stopwatch.StartNew();
        HttpResponseMessage? response = null;

        try
        {
            using HttpRequestMessage message = new(HttpMethod.Get, probeUri);
            if (level == ApiAlivenessLevel.Authenticated)
                await new ConfiguredApiAuthenticationProvider(identifier, config).ApplyAsync(message, cancellationToken).ConfigureAwait(false);

            response = await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
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
