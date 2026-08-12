using TestFramework.Web.Builder.Actions;

namespace TestFramework.Web.Builder.Stages;

/// <summary>
/// First stage of the API request builder: choose the method and path.
/// </summary>
public interface IApiConnectionStage : ISelectEndpointAction;

/// <summary>
/// Second stage of the API request builder: shape the request and call it.
/// </summary>
public interface IApiPayloadStage :
    IWithRouteValueAction,
    IWithQueryAction,
    IWithHeaderAction,
    IWithHeadersAction,
    IWithBodyAction,
    IWithAuthAction,
    ICallAction;
