<identity>
    <package>TestFramework.Web</package>
    <role>addon-skill</role>
</identity>

<objective>
    Explain how a TestFramework timeline calls REST APIs through TestFramework.Web: how an API is configured by identifier, how a request is composed from variables, what the step returns, how authentication is applied, and why a non-2xx response is a result rather than a failure.
</objective>

<package_scope>
    Covers WebExt.Api.Http(...) request building, WebExt.Api.IsLive(...) liveness probes, ApiConfig and the Api configuration section, authentication modes including Windows Negotiate, HttpResponseContext and its assertion helpers, the IHttpSender seam, and the web.restapi environment requirement kind.
    Also covers SQL Server: row artifacts, query finders, statement and script steps, scalar observations, liveness probes, and generating table definitions from registered models.
    Also covers stubbed dependencies: declaring what a stub answers, and asserting over its request log what the application under test actually sent outwards.
    Does not cover starting or hosting the application under test, its database or its stubs; that is the container lane's job.
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
    A database row has a key and a lifecycle, so it is an artifact, not a step. Three ways exist to put one in front of a test and they differ only in ownership: SetupArtifact plus AddArtifact creates and owns it, RegisterArtifact adopts an existing row by key and also owns it, and FindArtifact only observes what it locates. An owned row is removed on teardown; an observed row is left in place, and teardown records that it passed over it as information rather than as a failure.
    Statements that change data are steps in the Act phase. Aggregates are steps in the Observe phase, because a scalar has no identity and no lifecycle.
    The framework reaches SQL through a connection string and a model map, never through the application's own data access layer. The map comes from explicit registration, then DataAnnotations attributes, then convention.
    SqlSchema generates CREATE TABLE from a model map: schemas, tables, columns, nullability, identities and primary keys, and nothing else. It is scaffolding for a database the test owns, not a migration tool - a table generated from test-side models proves only that the models agree with themselves.
    A stub declaration is plain data with no delegates, because the server that runs it may be in another process or container and cannot call back into the test. Handlebars templating over the request covers what a callback would otherwise be for.
    Stub verification and waiting go through the stub server's own admin request log, polled over HTTP. The same assertions therefore run against a stub this run started, one the team runs permanently, or one started by hand - but those three are not equivalent, and the difference decides what the evidence is worth. Only a stub this run owns gives isolated evidence: on a shared stub the log also holds other runs' calls, made before this one and while it runs.
    WebExt.Stub.Reset defaults to a watermark: it reads the log, records the newest ReceivedAt from the stub's own clock, and later steps ignore everything at or before it. It deletes nothing. Set ResetMode to ClearServerLog for a stub the run owns; on a shared stub that would delete other runs' evidence. A call the stub logged without a timestamp stays in scope and produces one warning naming the stub.
    An unmatched stub call is the application asking a dependency for something the test never declared. Nothing else in a test reveals it. Assert the unmatched list is empty on a stub the run owns; on a shared stub the count is advisory, because another run's undeclared call lands in it.
    WithHeader(name, value) on Calls and Called is the only construct giving true isolation on a shared stub. The timeline cannot stamp a correlation id on the outbound call - the application under test makes it - but any application that forwards traceparent or a correlation header the test already set can be filtered on.
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
    Put an IsLive step in front of a host that may still be starting. It is the only step that waits: against a loopback or host.docker.internal authority it retries a 404 or 503 for LocalWarmupRetryDuration. An ordinary call is sent exactly once, so a timeline asserting NotFound gets its answer immediately.
    Let the step timeout be the only timeout unless a per-request transport limit is genuinely needed.
    Put additional secret-bearing header names in the Web:SensitiveHeaders configuration section rather than registering them in code.
    Turn on LogRequestHeaders through .ConfigureApiTrigger(...) when diagnosing authentication or routing; header values stay redacted.
    Declare lengths, precision, identities and required-ness on the model map when generating a schema; a CLR type cannot express them and the generator refuses to guess.
    Assert run.StubUnmatchedCalls(label).Should().HaveCount(0) alongside the positive assertions when the run owns the stub; it catches calls to endpoints the test never declared. Against a shared stub treat it as advisory and narrow the observation with WithHeader(...) instead.
    Narrow a stub wait with WithBodyContaining(...) when a run produces several calls to one endpoint, or the wait completes on the first one rather than the intended one.
</best_practices>

<api_hints>
    Important APIs and shapes from the package:
    - WebExt.Api.Http(identifier).Get|Post|Put|Patch|Delete(path).Call()
    - WithRouteValue(name, variable), WithQuery(name, variable), WithHeader(key, value), WithJsonBody(variable), WithBody(text|bytes, contentType), WithAuth(provider), WithBearerToken(variable)
    - WebExt.Api.IsLive(identifier, ApiAlivenessLevel.Reachable|Healthy|Authenticated). ApiIsLiveResult.Success and SqlIsLiveResult.Success are always true: a failed probe throws, and the field exists so an asserted probe reads like any other step result.
    - run.ApiStatus(label) | ApiBody(label) | ApiHeader(label, name) | ApiJson&lt;T&gt;(label) | ApiResponse(label) | ApiProbe(label) -> ValueHandle&lt;T&gt;, then .Should()...
    - run.Step(label).Should().HaveCompleted() | HaveThrown&lt;T&gt;(), run.AssertionScope()
    - run.Step("label").Response() | ProbeResult() for the raw typed result
    - HttpResponseContext: StatusCode, Body, Headers, ContentType, Elapsed, IsSuccess, Json&lt;T&gt;(), Header(name), BodyExcerpt()
    - ConfigInstance.FromJsonFile(path).LoadWebConfig().Build(), then timeline.SetupRun(config)
    - ApiConfig: BaseUrl, HealthPath, Auth, ApiKeyHeaderName, ApiKey, BearerToken, UserName, Password, RequestTimeout, AllowInvalidCertificates, UseCookies (off by default; the client is pooled per identifier, so a jar is shared by every run in the process)
    - ApiAuthMode: None, ApiKey, Bearer, Basic, Negotiate
    - Exceptions: ApiConfigurationValidationException, ApiRequestFailedException, ApiResponseFormatException, ApiLivenessProbeException
    - Setup: .LoadWebConfig(), .ConfigureApiTrigger(c =&gt; c with { ... }), .RedactHeaders(...)
    - WebExt.Artifact.Sql.Row&lt;T&gt;(identifier, keyValues...) with SetupArtifact/AddArtifact, payload TestFramework.Web.Sql.Artifacts.SqlRowArtifactData&lt;T&gt;. TestFramework.Azure declares a type of the same name; in a file using both, alias one with a using rather than expecting a rename.
    - WebExt.ArtifactFinder.Sql.Where&lt;T&gt;(identifier, "Name = @name").WithParameter("name", variable) with FindArtifact/FindArtifacts
    - timeline.RegisterArtifact(identifier, WebExt.Artifact.Sql.Row&lt;T&gt;(...)) to adopt a row the application wrote
    - WebExt.Sql.Execute|Scalar&lt;T&gt;|Script(identifier, ...).WithParameter(name, variable). A script's GO batches all run on one connection, so #temp tables and SET options survive a GO; GO 3 repeats a batch. A custom ISqlExecutor keeps the old per-batch behaviour until it overrides ExecuteScriptAsync.
    - WebExt.Sql.IsLive(identifier, SqlAlivenessLevel.Reachable|Database). The connection string names the catalog, so both levels have already opened the configured database; they differ only in SELECT 1 against SELECT DB_NAME().
    - run.SqlRow&lt;T&gt;(artifactId) | SqlScalar&lt;T&gt;(label) | SqlAffectedRows(label) | SqlProbe(label) -> ValueHandle&lt;T&gt;
    - SqlConfig: ConnectionString or Server/Database/IntegratedSecurity/UserName/Password/TrustServerCertificate/CommandTimeout
    - Setup: .AddWebSqlModels(models =&gt; models.For&lt;T&gt;().Table("...").Key(x =&gt; x.Id).Generated(x =&gt; x.Id)), .ConfigureSqlSteps(...), .UseSqlCredentials(...)
    - SqlSchema.CreateTablesScript(types...) | CreateTable(map) | CreateTables(maps) -> DDL from a model map
    - Model map extras for generation: .Identity(x =&gt; x.Id), .MaxLength(x =&gt; x.Name, 200), .Precision(x =&gt; x.Total, 18, 2), .Required(x =&gt; x.Name), .ColumnType(x =&gt; x.Amount, "money")
    - StubDefinition with Configure(StubMappingBuilder): .OnGet|OnPost|OnPut|OnDelete(path).WithHeader|WithQuery|WithBodyContaining|WithPriority(...).RespondJson|RespondText|RespondStatus(...)
    - WebExt.Stub.Called(identifier, method, path).WithBodyContaining(variable).WithHeader(name, value) as a WaitForEvent source
    - WebExt.Stub.Calls(identifier, method?, path?).WithHeader(name, value) | WebExt.Stub.Reset(identifier). The path filter takes the same * wildcard the mappings take, and a leading slash is optional.
    - run.StubCall(label) | StubCalls(label) | StubUnmatchedCalls(label) -> ValueHandle&lt;T&gt;
    - StubConfig: BaseUrl, AdminPath, PollInterval, AllowInvalidCertificates, ResetMode (Watermark by default, or ClearServerLog)
    - Extension points: IWebComponentFactory, IHttpSender, IApiConfigProvider, IApiAuthenticationProvider, ISqlExecutor, ISqlModelMapSource, ISqlCredentialProvider, ISqlConfigProvider, IStubConfigProvider
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
      "Stub": {
        "payments": { "BaseUrl": "http://localhost:9091/", "ResetMode": "Watermark" }
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
    Do not delete rows the application created; only rows the test seeded or explicitly adopted are its own.
    Do not use RegisterArtifact for a row that existed before the test ran: adopting it means owning it, and owning it means removing it.
    Do not concatenate values into SQL; parameters are variable-backed for exactly that reason.
    Do not assert with a third-party fluent-assertion package; the framework has its own.
    Do not generate a schema from models for a database whose schema is owned elsewhere; mirror the real schema with a script instead.
    Do not expect a stub declaration to run C# on a request; it is data, and the server may be in another container.
</anti_patterns>
