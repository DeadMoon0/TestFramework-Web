using System;
using System.Collections.Generic;
using System.Net.Http;
using TestFramework.Core.Steps;
using TestFramework.Core.Variables;
using TestFramework.Web.Auth;
using TestFramework.Web.Builder.Stages;
using TestFramework.Web.Http;
using TestFramework.Web.Identifier;
using TestFramework.Web.Trigger;

namespace TestFramework.Web.Builder;

/// <summary>
/// Builds an API request against a configured identifier.
/// </summary>
internal sealed class RemoteApiBuilder(ApiIdentifier identifier) : IApiConnectionStage, IApiPayloadStage
{
    private readonly ApiRequestBuilderState _state = new();

    public IApiPayloadStage Get(string path) => Method(HttpMethod.Get, RequirePath(path));

    public IApiPayloadStage Get(VariableReference<string> path) => Method(HttpMethod.Get, path);

    public IApiPayloadStage Post(string path) => Method(HttpMethod.Post, RequirePath(path));

    public IApiPayloadStage Post(VariableReference<string> path) => Method(HttpMethod.Post, path);

    public IApiPayloadStage Put(string path) => Method(HttpMethod.Put, RequirePath(path));

    public IApiPayloadStage Put(VariableReference<string> path) => Method(HttpMethod.Put, path);

    public IApiPayloadStage Patch(string path) => Method(HttpMethod.Patch, RequirePath(path));

    public IApiPayloadStage Delete(string path) => Method(HttpMethod.Delete, RequirePath(path));

    public IApiPayloadStage Method(HttpMethod method, VariableReference<string> path)
    {
        _state.SetEndpoint(method, path);
        return this;
    }

    public IApiPayloadStage WithRouteValue(string name, VariableReference<string> value)
    {
        _state.AddRouteValue(name, value);
        return this;
    }

    public IApiPayloadStage WithQuery(string name, VariableReference<string> value)
    {
        _state.AddQuery(name, value);
        return this;
    }

    public IApiPayloadStage WithHeader(VariableReference<string> key, VariableReference<string> value)
    {
        _state.AddHeader(key, value);
        return this;
    }

    public IApiPayloadStage WithHeader(VariableReference<string> key, VariableReference<string[]> values)
    {
        _state.AddHeader(key, values);
        return this;
    }

    public IApiPayloadStage WithHeaders(VariableReference<Dictionary<string, string>> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        // Header dictionaries resolve at run time, so each entry is projected into its own
        // key/value pair variable rather than being expanded at build time.
        _state.AddHeaderDictionary(headers);
        return this;
    }

    public IApiPayloadStage WithBody(VariableReference<string> text, string? contentType = null)
    {
        _state.SetBody(text, contentType ?? "text/plain; charset=utf-8");
        return this;
    }

    public IApiPayloadStage WithBody(VariableReference<byte[]> data, string? contentType = null)
    {
        _state.SetBody(data, contentType ?? "application/octet-stream");
        return this;
    }

    public IApiPayloadStage WithJsonBody<TValue>(VariableReference<TValue> value)
    {
        _state.SetJsonBody(value);
        return this;
    }

    public IApiPayloadStage WithAuth(IApiAuthenticationProvider provider)
    {
        _state.SetAuthOverride(provider);
        return this;
    }

    public IApiPayloadStage WithBearerToken(VariableReference<string> token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _state.SetAuthOverride(new BearerTokenVariableAuthenticationProvider(token));
        return this;
    }

    public Step<HttpResponseContext> Call()
    {
        ComposedRequestVariable request = _state.BuildVariable();
        return new HttpApiTrigger(identifier, request);
    }

    private static VariableReference<string> RequirePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Var.Const(path);
    }
}
