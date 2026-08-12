<identity>
    <package>TestFramework.Web</package>
    <role>addon-skill</role>
</identity>

<objective>
    Explain how a TestFramework timeline calls REST APIs through TestFramework.Web: how an API is configured by identifier, how a request is composed from variables, what the step returns, how authentication is applied, and why a non-2xx response is a result rather than a failure.
</objective>

<package_scope>
    Covers WebExt.Api.Http(...) request building, WebExt.Api.IsLive(...) liveness probes, ApiConfig and the Api configuration section, authentication modes including Windows Negotiate, HttpResponseContext and its assertion helpers, the IHttpSender seam, and the web.restapi environment requirement kind.
    Does not cover ASP.NET in-process hosting, SQL Server assertions, container hosting or UI tests. Those are planned additions to the same package.
</package_scope>

<key_concepts>
    TestFramework.Web does not replace the Core timeline model. A request is an ordinary step and the response is an ordinary step result.
    APIs are addressed by logical identifier, never by a literal URL in the timeline. The identifier resolves through WebConfigStore&lt;ApiConfig&gt;, populated from the Api:&lt;identifier&gt; configuration section by LoadWebConfig().
    A non-2xx status code is returned as data. Only transport failures - connection refused, DNS failure, timeout - raise ApiRequestFailedException. Use ExpectStatus/ExpectSuccess/ExpectJson when a status should be asserted.
    Endpoints are addressed by explicit path and method. The framework deliberately does not derive routes from controller types, so the test never references the application project and a server-side route change fails the test.
    Every request part is variable-backed: path, route values, query, headers and body. The trigger declares identifier-backed inputs through DeclareIO.
    IHttpSender is the seam that decides how a request travels. The same timeline therefore works against a deployed API today and against an in-process or containerized host later.
    Steps declare the environment requirement kind web.restapi. The active environment provider, if any, decides how that requirement is satisfied.
    HttpResponseContext is plain serializable data because step results travel to the debugging UI. A live HttpResponseMessage never leaves the trigger.
</key_concepts>

<best_practices>
    Configure one identifier per API and keep base URLs out of timelines entirely.
    Assert the status code before reading the body. ExpectJson does this for you; a raw Json&lt;T&gt;() call on an error payload produces a confusing format error instead of a clear status failure.
    Use variables for anything that changes between runs, including paths built from earlier step results. A hardcoded string in a step breaks build-once-run-many.
    Prefer WithRouteValue over string concatenation. It escapes the value and fails loudly when a token is left unsubstituted.
    For APIs behind Windows integrated authentication, set Auth to Negotiate rather than changing the application.
    Use per-request WithBearerToken or WithAuth when a token comes from an earlier step; configuration is for static credentials.
    Wait for a slow host with IsLive at Reachable level plus .WithTimeOut(...) and .WithRetry(...), not with sleeps.
    Let the step timeout be the only timeout unless a per-request transport limit is genuinely needed.
    Register additional secret-bearing header names with HttpHeaderRedaction.AddSensitiveHeader so they never reach logs.
    Prefer run assertions such as run.Step("x").ExpectStatus(...) over manual comparisons: they produce diagnosable failure messages.
</best_practices>

<api_hints>
    Important APIs and shapes from the package:
    - WebExt.Api.Http(identifier).Get|Post|Put|Patch|Delete(path).Call()
    - WithRouteValue(name, variable), WithQuery(name, variable), WithHeader(key, value), WithJsonBody(variable), WithBody(text|bytes, contentType), WithAuth(provider), WithBearerToken(variable)
    - WebExt.Api.IsLive(identifier, ApiAlivenessLevel.Reachable|Healthy|Authenticated)
    - run.Step("label").Response() | ExpectStatus(code) | ExpectSuccess() | ExpectJson&lt;T&gt;() | ProbeResult()
    - HttpResponseContext: StatusCode, Body, Headers, ContentType, Elapsed, IsSuccess, Json&lt;T&gt;(), Header(name), BodyExcerpt()
    - ConfigInstance.FromJsonFile(path).LoadWebConfig().Build(), then timeline.SetupRun(config)
    - ApiConfig: BaseUrl, HealthPath, Auth, ApiKeyHeaderName, ApiKey, BearerToken, UserName, Password, RequestTimeout, AllowInvalidCertificates
    - ApiAuthMode: None, ApiKey, Bearer, Basic, Negotiate
    - Exceptions: ApiConfigurationValidationException, ApiRequestFailedException, ApiStatusAssertionException, ApiResponseFormatException, ApiLivenessProbeException
    - Extension points: IWebComponentFactory, IHttpSender, IApiConfigProvider, IApiAuthenticationProvider
</api_hints>

<configuration_shape>
    {
      "Api": {
        "sample": {
          "BaseUrl": "http://localhost:5080/",
          "HealthPath": "/health",
          "Auth": "None",
          "RequestTimeout": "00:00:30"
        }
      }
    }
</configuration_shape>

<anti_patterns>
    Do not put base URLs or absolute URIs in timeline steps.
    Do not reference the application project to discover routes.
    Do not treat a 404 or 401 as an exception path; assert it.
    Do not read the body before asserting the status.
    Do not add sleeps to wait for a host; use IsLive with a timeout and retry.
    Do not log credential headers or put secrets in variables that are bound into results.
</anti_patterns>
