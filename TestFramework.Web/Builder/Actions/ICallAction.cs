using TestFramework.Core.Steps;
using TestFramework.Web.Http;

namespace TestFramework.Web.Builder.Actions;

/// <summary>
/// Materializes the composed request into an executable step.
/// </summary>
public interface ICallAction
{
    /// <summary>
    /// Builds the step that sends the request and captures the response.
    /// </summary>
    /// <returns>The step to place on the timeline.</returns>
    Step<HttpResponseContext> Call();
}
