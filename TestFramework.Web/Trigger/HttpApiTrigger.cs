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

namespace TestFramework.Web.Trigger;

/// <summary>
/// Sends an HTTP request to a configured API and captures the response.
/// </summary>
/// <remarks>
/// An unsuccessful status code is a result, not a failure: it is returned so the timeline can
/// assert on it. Only transport failures raise <see cref="ApiRequestFailedException"/>.
/// </remarks>
internal sealed class HttpApiTrigger(ApiIdentifier identifier, ComposedRequestVariable request)
    : Step<HttpResponseContext>, IHasEnvironmentRequirements
{
    public override string Name => "Http API Trigger";

    public override string Description => $"Sends an HTTP request to the API '{identifier}'";

    public override bool DoesReturn => true;

    public override Step<HttpResponseContext> Clone() => new HttpApiTrigger(identifier, request).WithClonedOptions(this);

    public override StepInstance<Step<HttpResponseContext>, HttpResponseContext> GetInstance() =>
        new StepInstance<Step<HttpResponseContext>, HttpResponseContext>(this);

    public override void DeclareIO(StepIOContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        foreach (VariableReferenceGeneric input in request.Inputs)
        {
            if (input.Identifier is { } variableIdentifier)
                contract.Inputs.Add(new StepIOEntry(variableIdentifier.Identifier, StepIOKind.Variable, true, typeof(string)));
        }
    }

    public IReadOnlyCollection<EnvironmentRequirement> GetEnvironmentRequirements(VariableStore variableStore)
        => [new EnvironmentRequirement(WebEnvironmentResourceKinds.RestApi, identifier)];

    public override async Task<HttpResponseContext?> Execute(
        IServiceProvider serviceProvider,
        VariableStore variableStore,
        ArtifactStore artifactStore,
        ScopedLogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        HttpRequestSpec spec = request.GetRequiredValue(variableStore, "API request");
        ApiConfig config = ApiConfigResolver.Resolve(serviceProvider, identifier);
        ApiTriggerConfig triggerConfig = serviceProvider.GetService<ApiTriggerConfig>() ?? new ApiTriggerConfig();

        Uri requestUri = spec.ResolveUri(identifier, config.BaseUrl);
        IHttpSender sender = serviceProvider.GetWebComponentFactory().CreateSender(identifier, config);
        IApiAuthenticationProvider authentication = ResolveAuthentication(spec, variableStore, config);

        if (triggerConfig.LogRequests)
            logger.LogInformation("API '{0}' -> {1} {2}", identifier, spec.Method, requestUri);

        Stopwatch stopwatch = Stopwatch.StartNew();
        HttpResponseMessage? response = null;

        try
        {
            response = await SendWithLocalWarmupRetryAsync(
                sender,
                spec,
                requestUri,
                authentication,
                triggerConfig,
                logger,
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            if (triggerConfig.LogRequests)
                logger.LogInformation("API '{0}' <- {1} in {2}", identifier, response.StatusCode, stopwatch.Elapsed);

            return await HttpResponseContext.FromHttpResponseAsync(
                identifier,
                spec.Method,
                requestUri,
                response,
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            stopwatch.Stop();
            throw ApiRequestFailedException.Transport(identifier, spec.Method, requestUri, stopwatch.Elapsed, exception);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private IApiAuthenticationProvider ResolveAuthentication(HttpRequestSpec spec, VariableStore variableStore, ApiConfig config)
    {
        if (spec.AuthOverride is IVariableBackedAuthenticationProvider variableBacked)
            return variableBacked.Resolve(variableStore);

        return spec.AuthOverride ?? new ConfiguredApiAuthenticationProvider(identifier, config);
    }

    private async Task<HttpResponseMessage> SendWithLocalWarmupRetryAsync(
        IHttpSender sender,
        HttpRequestSpec spec,
        Uri requestUri,
        IApiAuthenticationProvider authentication,
        ApiTriggerConfig triggerConfig,
        ScopedLogger logger,
        CancellationToken cancellationToken)
    {
        // A freshly started local host answers 404 or 503 until its routes are live. Callers should
        // not have to model that startup quirk as a user-visible step retry.
        bool retryWarmup = IsLocalDevelopmentHost(requestUri) && triggerConfig.LocalWarmupRetryDuration > TimeSpan.Zero;
        DateTime deadline = DateTime.UtcNow.Add(triggerConfig.LocalWarmupRetryDuration);

        while (true)
        {
            using HttpRequestMessage message = spec.CreateMessage(requestUri);
            await authentication.ApplyAsync(message, cancellationToken).ConfigureAwait(false);

            HttpResponseMessage response = await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
            bool warmupStatus = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.ServiceUnavailable;

            if (!retryWarmup || !warmupStatus || DateTime.UtcNow >= deadline)
                return response;

            logger.LogWarning(
                "API '{0}' warmup retry after {1} from local host '{2}'. Retrying in {3}.",
                identifier,
                response.StatusCode,
                requestUri.Authority,
                triggerConfig.LocalWarmupRetryDelay);

            response.Dispose();
            await Task.Delay(triggerConfig.LocalWarmupRetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsLocalDevelopmentHost(Uri uri)
        => uri.IsLoopback
        || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase);
}
