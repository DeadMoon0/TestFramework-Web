using TestFramework.Core.Steps;
using TestFramework.Core.Variables;
using TestFramework.Web.Builder;
using TestFramework.Web.Builder.Stages;
using TestFramework.Web.Identifier;
using TestFramework.Web.Trigger.IsLive;

namespace TestFramework.Web;

/// <summary>
/// Entry point for the web-specific TestFramework DSL.
/// </summary>
public static class WebExt
{
    /// <summary>
    /// Access REST API triggers and liveness probes.
    /// </summary>
    public static ApiProxy Api { get; } = new ApiProxy();

    /// <summary>
    /// Creates REST API steps for a configured identifier.
    /// </summary>
    public class ApiProxy
    {
        /// <summary>
        /// Starts an HTTP request builder for a configured API identifier.
        /// </summary>
        /// <param name="identifier">The API identifier to resolve.</param>
        /// <returns>The builder stage used to select a method and path.</returns>
        public IApiConnectionStage Http(ApiIdentifier identifier) => new RemoteApiBuilder(identifier);

        /// <summary>
        /// Creates a liveness probe using a constant level.
        /// </summary>
        /// <param name="identifier">The API identifier to probe.</param>
        /// <param name="level">How deep the probe should go.</param>
        /// <returns>A step that probes the API when executed.</returns>
        public Step<ApiIsLiveResult> IsLive(ApiIdentifier identifier, ApiAlivenessLevel level = ApiAlivenessLevel.Reachable)
            => IsLive(identifier, Var.Const(level));

        /// <summary>
        /// Creates a liveness probe using a variable-backed level.
        /// </summary>
        /// <param name="identifier">The API identifier to probe.</param>
        /// <param name="level">The variable carrying the probe depth.</param>
        /// <returns>A step that probes the API when executed.</returns>
        public Step<ApiIsLiveResult> IsLive(ApiIdentifier identifier, VariableReference<ApiAlivenessLevel> level)
            => new ApiIsLiveTrigger(identifier, level);
    }
}
