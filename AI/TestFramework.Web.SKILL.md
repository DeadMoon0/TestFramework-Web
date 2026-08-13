<identity>
    <package>TestFramework.Web</package>
    <role>addon-skill</role>
</identity>

<objective>
    Explain how a TestFramework timeline calls REST APIs through TestFramework.Web: how an API is configured by identifier, how a request is composed from variables, what the step returns, how authentication is applied, and why a non-2xx response is a result rather than a failure.
</objective>

<package_scope>
    Covers WebExt.Api.Http(...) request building, WebExt.Api.IsLive(...) liveness probes, ApiConfig and the Api configuration section, authentication modes including Windows Negotiate, HttpResponseContext and its assertion helpers, the IHttpSender seam, and the web.restapi environment requirement kind.
    Also covers SQL Server: row artifacts, query finders, statement and script steps, scalar observations and liveness probes.
    Does not cover starting or hosting the application under test or its database; that is the container lane's job.
</package_scope>

<key_concepts>
    TestFramework.Web does not replace the Core timeline model. A request is an ordinary step and the response is an ordinary step result.
    APIs are addressed by logical identifier, never by a literal URL in the timeline. The identifier resolves through WebConfigStore&lt;ApiConfig&gt;, populated from the Api:&lt;identifier&gt; configuration section by LoadWebConfig().
    A non-2xx status code is returned as data. Only transport failures - connection refused, DNS failure, timeout - raise ApiRequestFailedException. Assert the status with the in-house fluent assertions instead.
    Endpoints are addressed by explicit path and method. The framework deliberately does not derive routes from controller types, so the test never references the application project and a server-side route change fails the test.
    Every request part is variable-backed: path, route values, query, headers and body. The trigger declares identifier-backed inputs through DeclareIO.
    IHttpSender is the seam that decides how a request travels. The same timeline therefore works against a deployed API today and against an in-process or containerized host later.
    Steps declare the environment requirement kind web.restapi. The active environment provider, if any, decides how that requirement is satisfied.
    HttpResponseContext is plain serializable data because step results travel to the debugging UI. A live HttpResponseMessage never leaves the trigger.
    A database row has a key and a lifecycle, so it is an artifact, not a step. Rows the test seeds are upserted on setup and deleted on teardown; rows located by a finder are observed and never deleted.
    Statements that change data are steps in the Act phase. Aggregates are steps in the Observe phase, because a scalar has no identity and no lifecycle.
    The framework reaches SQL through a connection string and a model map, never through the application's own data access layer. The map comes from explicit registration, then DataAnnotations attributes, then convention.
</key_concepts>

<best_practices>
    Configure one identifier per API and keep base URLs out of timelines entirely.
    Assert with the in-house fluent assertions: run.ApiStatus(label).Should().Be(...), run.ApiBody(label).Should().Contain(...), run.ApiJson&lt;T&gt;(label).Should().HaveCount(...). They are signalled to the debugging UI and participate in run.AssertionScope(). Do not hand-roll comparisons with a third-party assertion library.
    Assert the status code before reading the body; an error payload rarely uses the success schema.
    Use variables for anything that changes between runs, including paths built from earlier step results. A hardcoded string in a step breaks build-once-run-many.
    Prefer WithRouteValue over string concatenation. It escapes the value and fails loudly when a token is left unsubstituted.
    For APIs behind Windows integrated authentication, set Auth to Negotiate rather than changing the application.
    Use per-request WithBearerToken or WithAuth when a token comes from an earlier step; configuration is for static credentials.
    Wait for a slow host with IsLive at Reachable level plus .WithTimeOut(...) and .WithRetry(...), not with sleeps.
    Let the step timeout be the only timeout unless a per-request transport limit is genuinely needed.
    Put additional secret-bearing header names in the Web:SensitiveHeaders configuration section rather than registering them in code.
    Turn on LogRequestHeaders through .ConfigureApiTrigger(...) when diagnosing authentication or routing; header values stay redacted.
</best_practices>

<api_hints>
    Important APIs and shapes from the package:
    - WebExt.Api.Http(identifier).Get|Post|Put|Patch|Delete(path).Call()
    - WithRouteValue(name, variable), WithQuery(name, variable), WithHeader(key, value), WithJsonBody(variable), WithBody(text|bytes, contentType), WithAuth(provider), WithBearerToken(variable)
    - WebExt.Api.IsLive(identifier, ApiAlivenessLevel.Reachable|Healthy|Authenticated)
    - run.ApiStatus(label) | ApiBody(label) | ApiHeader(label, name) | ApiJson&lt;T&gt;(label) | ApiResponse(label) | ApiProbe(label) -> ValueHandle&lt;T&gt;, then .Should()...
    - run.Step(label).Should().HaveCompleted() | HaveThrown&lt;T&gt;(), run.AssertionScope()
    - run.Step("label").Response() | ProbeResult() for the raw typed result
    - HttpResponseContext: StatusCode, Body, Headers, ContentType, Elapsed, IsSuccess, Json&lt;T&gt;(), Header(name), BodyExcerpt()
    - ConfigInstance.FromJsonFile(path).LoadWebConfig().Build(), then timeline.SetupRun(config)
    - ApiConfig: BaseUrl, HealthPath, Auth, ApiKeyHeaderName, ApiKey, BearerToken, UserName, Password, RequestTimeout, AllowInvalidCertificates
    - ApiAuthMode: None, ApiKey, Bearer, Basic, Negotiate
    - Exceptions: ApiConfigurationValidationException, ApiRequestFailedException, ApiResponseFormatException, ApiLivenessProbeException
    - Setup: .LoadWebConfig(), .ConfigureApiTrigger(c =&gt; c with { ... }), .RedactHeaders(...)
    - WebExt.Artifact.Sql.Row&lt;T&gt;(identifier, keyValues...) with SetupArtifact/AddArtifact
    - WebExt.ArtifactFinder.Sql.Where&lt;T&gt;(identifier, "Name = @name").WithParameter("name", variable) with FindArtifact/FindArtifacts
    - WebExt.Sql.Execute|Scalar&lt;T&gt;|Script(identifier, ...).WithParameter(name, variable)
    - WebExt.Sql.IsLive(identifier, SqlAlivenessLevel.Reachable|Database)
    - run.SqlRow&lt;T&gt;(artifactId) | SqlScalar&lt;T&gt;(label) | SqlAffectedRows(label) | SqlProbe(label) -> ValueHandle&lt;T&gt;
    - SqlConfig: ConnectionString or Server/Database/IntegratedSecurity/UserName/Password/TrustServerCertificate/CommandTimeout
    - Setup: .AddWebSqlModels(models =&gt; models.For&lt;T&gt;().Table("...").Key(x =&gt; x.Id).Generated(x =&gt; x.Id)), .ConfigureSqlSteps(...), .UseSqlCredentials(...)
    - Extension points: IWebComponentFactory, IHttpSender, IApiConfigProvider, IApiAuthenticationProvider, ISqlExecutor, ISqlModelMapSource, ISqlCredentialProvider, ISqlConfigProvider
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
      },
      "Sql": {
        "main": { "Server": "localhost,1433", "Database": "SampleDb", "IntegratedSecurity": true, "TrustServerCertificate": true }
      },
      "Web": {
        "SensitiveHeaders": [ "x-tenant-secret" ]
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
    Do not model a database row as a step; it is an artifact.
    Do not delete rows the application created; only rows the test seeded are its own.
    Do not concatenate values into SQL; parameters are variable-backed for exactly that reason.
    Do not assert with a third-party fluent-assertion package; the framework has its own.
</anti_patterns>
