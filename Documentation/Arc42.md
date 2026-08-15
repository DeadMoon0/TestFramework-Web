# TestFramework.Web - Architecture Notes

## 1. Introduction And Goals

`TestFramework.Web` makes an HTTP API testable as a first-class timeline participant. A request is a
step, a response is a step result, and every part of the request is variable-backed so one timeline
runs many times with different data.

The driving quality goal is **portability of the timeline**: the same steps must run against a
deployed API, an in-process host and a container, with only the environment differing.

## 2. Constraints

- Targets `net8.0` and `net10.0`. The in-process hosting lane, when it lands, will require the test
  process to target at least the application's framework version.
- Depends only on `TestFramework.Core`, `TestFramework.Config` and
  `Microsoft.Extensions.Configuration`/`DependencyInjection`. No ASP.NET reference in the REST lane.
- Step results travel to the WPF debugging UI, so results must be plain serializable data.
- Zero compiler warnings with `TreatWarningsAsErrors`; all public API carries XML documentation.

## 3. Context

```
Timeline (Core)
  -> HttpApiTrigger / ApiIsLiveTrigger        steps
       -> WebConfigStore<ApiConfig>           what to call
       -> IHttpSender                         how to call it
            -> real endpoint | in-process host | container
```

The module declares the environment requirement kind `web.restapi`. It does not implement an
environment provider: a step says what it needs, and whichever provider is active decides how that
need is met.

## 4. Solution Strategy

| Concern | Decision |
|---|---|
| Addressing an API | Logical identifier resolved through `WebConfigStore<ApiConfig>`, never a literal URL in a timeline |
| Transport indirection | `IHttpSender`, resolved from `IWebComponentFactory` |
| Endpoint selection | Explicit path and method; deliberately not derived from application types |
| Request composition | One `HttpRequestSpec` owns URI composition, escaping and message creation |
| Failure semantics | Status codes are results; only transport problems throw |
| Credentials | Message-level modes in `IApiAuthenticationProvider`; `Negotiate` on the handler |

## 5. Building Block View

**`Configuration/`** — `ApiConfig` plus `WebConfigStore<TConfig>`, `IApiConfigProvider` and its
default reading the `Api:<identifier>` section. `ApiConfigResolver` centralizes lookup so the
"unknown identifier" message is identical everywhere. The store is registered even when no `Api`
section exists, so a future environment can hydrate identifiers at run time.

**`Http/`** — `HttpRequestSpec` (resolved request, URI composition, message creation),
`ApiRequestBuilderState` and `ComposedRequestVariable` (variable accumulation and resolution),
`HttpResponseContext` (the step result), `IHttpSender`, `HttpHeaderRedactor`.

**`Builder/`** — two staged interfaces, one action interface per capability, implemented by
`RemoteApiBuilder`. `Call()` materializes the step.

**`Trigger/`** — `HttpApiTrigger` and `IsLive/ApiIsLiveTrigger`, plus `ApiTriggerConfig` for
liveness-probe warmup tuning and request logging.

**`Auth/`** — the mode enum, the provider interface, the configuration-driven provider, a delegate
provider, and a variable-backed bearer provider resolved against the store at execution time.

**`Runtime/`** — `IWebComponentFactory` and the default implementation pooling `HttpClient`
instances per identifier and per connection-relevant setting.

**`Sql/`** — `SqlConfig` and its store, the model map (`Model/`) resolved from explicit
registrations, then attributes, then convention, the statement builder and ADO executor
(`Execution/`), row artifacts and finders (`Artifacts/`), Act and Observe steps (`Steps/`), and
`Schema/` generating table definitions from a map.

**`Stub/`** — `StubDefinition` and the mapping model (`Mappings/`), serialized to the JSON a stub
server loads; `Admin/` reading the server's request log over HTTP; `Steps/` providing the wait event
and the observation. The package declares and verifies stubs; it never hosts one.

## 6. Runtime View

A single API step:

1. `ComposedRequestVariable.GetValue` resolves every variable into an `HttpRequestSpec`.
2. `ApiConfigResolver` resolves the `ApiConfig`, failing with the registered identifiers listed.
3. `HttpRequestSpec.ResolveUri` composes the absolute URI, substituting and escaping route values
   and the query, and rejects unsubstituted tokens.
4. `IWebComponentFactory.CreateSender` returns a pooled sender for that identifier.
5. Authentication is applied — the per-request override wins over the configured mode.
6. The request is sent exactly once; a status code is a result, so nothing is retried here.
7. The response is converted to `HttpResponseContext`; the live `HttpResponseMessage` is disposed
   inside the step and never escapes.

## 7. Deployment View

The package is a test-time dependency. The REST lane needs no infrastructure: the integration tests
in this repository start the bundled sample API on an ephemeral loopback port.

## 8. Crosscutting Concepts

**Variables everywhere.** Path, route values, query, headers and body are `VariableReference<T>`.
The trigger declares identifier-backed inputs through `DeclareIO`, which is what lets the framework
reason about data flow between steps.

**Redaction.** Credential-bearing headers are redacted before any value reaches a log or debug
envelope. The policy is a configured `WebRedactionOptions` resolved from the run services rather than
global mutable state, so one test project cannot change what another one hides.

**Assertions are the framework's own.** The module exposes `ValueHandle<T>` entry points obtained
through `TimelineRun.Assert(...)` instead of its own comparison helpers, so web assertions are
signalled to the debugging UI, honour `run.AssertionScope()` and throw the Core exception types.
`HttpResponseContext.ToString()` supplies the diagnostic context those messages render.

**Serializable results.** `HttpResponseContext` is a record of primitives and dictionaries. Bounded
body excerpts (2 KB) keep failure messages and the debug transport from being flooded.

**One timeout.** Without a configured `RequestTimeout`, the step timeout governs. Two knobs that can
disagree is a defect, not a feature.

## 9. Decisions

| Decision | Rationale |
|---|---|
| No route analyzer over controller types | An API is tested at its edge. Reflecting over `[Route]` couples the test to the implementation and would let a route rename pass silently. It would also force a project reference to the application. |
| Non-2xx is not an exception | Tests routinely assert error behaviour. Throwing would make the common case awkward and the rare case no clearer. |
| Own HTTP primitives rather than reusing `TestFramework.Azure` | The Azure package carries Cosmos, Service Bus and EF Core. Consolidating the duplicated primitives into a shared package is planned once the shape settles. |
| Own `WebConfigStore<T>` | `ConfigStore<T>` lives in `TestFramework.Azure`. A distinct name lets both be imported in one test file. |
| Assertions reuse Core asserters rather than adding module-specific ones | The assertion infrastructure - debug signalling and scope collection - is internal to Core. Wrapping values into `ValueHandle<T>` keeps web assertions first-class instead of a parallel mechanism that bypasses the debugging UI. |
| Warmup retry lives in the liveness probe, and only for local hosts | A transient `404` while a local route table warms up is infrastructure noise, and waiting it out is exactly what `IsLive` is for. Putting the same retry on every call would make each deliberate `404` assertion pay the full window. On a remote host the status is a real answer and must not be hidden. |
| Pooled clients keyed by connection-relevant settings | Connection reuse across steps, while a run that rewrites the endpoint still gets a fresh client. |
| The client pool is bounded at 64 and evicts the least recently used | The key includes the base URL, and a container lane hands out a new ephemeral port per run. Unbounded, a long-lived host accumulates a client, a handler, a socket pool and a DNS cache per run and disposes none of them. |
| `SocketsHttpHandler` with `PooledConnectionLifetime` | A client reused across runs otherwise keeps the address it resolved the first time. Recycling connections after two minutes is the documented way to let DNS changes through. `Negotiate` becomes `Credentials = CredentialCache.DefaultCredentials`, which is what `HttpClientHandler.UseDefaultCredentials` sets on its own underlying handler. |
| A database row is an artifact, a statement is a step, an aggregate is an observation | A row has identity and a lifecycle; a statement is an action; a scalar has neither. Modelling them alike would give rows no teardown and statements no ordering. |
| Generated schema covers tables and keys only | Foreign keys, indexes and constraints belong to a schema someone owns. Generating them would imply this is a migration tool, which it is not. |
| A script's batches run on one connection | `GO` is a client-side separator, not a transaction boundary. A `#temp` table, a `SET` option, `SCOPE_IDENTITY()` and a transaction spanning a `GO` are all connection state, and a connection per batch loses every one of them silently. Pooling does not help: the pool issues `sp_reset_connection`. |
| `ExecuteScriptAsync` is a default interface method | `ISqlExecutor` is the advertised extensibility seam and is public. A required member would break every external implementer; the default body keeps them compiling with the old per-batch semantics until they override it. |
| Stub mappings carry no delegates | The server that runs them may be in another process or container and cannot call back. Templating over the request covers what a callback would be for, and keeps one declaration valid wherever it runs. |
| Stub verification polls the admin log | A push channel would require the stub to reach the test process, which a container cannot do without exposing a host listener. Polling works identically everywhere. |
| Stub reset records a watermark instead of deleting the log | The request log is global. On a stub several runs share, `DELETE /__admin/requests` destroys evidence that is not this run's to destroy. The watermark uses the stub's own clock, which is the only clock comparable with the log's timestamps. Deleting stays available as `ResetMode: ClearServerLog` for a stub the run owns. |

## 10. Quality Requirements

| Requirement | How it is met |
|---|---|
| A failure is diagnosable without re-running | Request, status, elapsed time, correlation headers and body excerpt in the message |
| Timelines survive hosting changes | `IHttpSender` plus identifier-based configuration |
| No secret leaks | Redaction list applied to logs and debug values |
| Both target frameworks behave identically | The full suite runs on `net8.0` and `net10.0` in CI |

## 11. Risks And Technical Debt

- HTTP primitives are duplicated with `TestFramework.Azure`. Intentional for now; consolidation is a
  planned step that touches a published package.
- `ComposedRequestVariable.GetValue` is covered end to end rather than by unit test, because
  `VariableStore` has an internal constructor that this assembly cannot reach.
- `DeclareIO` reports every input as `typeof(string)`. Adequate for the current contract validator,
  but it loses type information for non-string request parts.
- Generated schema is derived from test-side models, so it can drift from a schema owned elsewhere
  without any test failing. Comparing a map against a live schema is the planned answer.
- `WebExt.Stub.Calls(...)` filters on method, path (with `*` wildcards) and headers; a body
  predicate exists on the wait event but not on the observation.
- On a stub shared with other runs, the reset watermark separates this run from everything logged
  *before* it, but nothing separates it from a run happening *at the same time* except a header
  filter — and only when the application under test forwards a header the test set.
- Stub verification depends on the shape of the stub server's admin request log. It is pinned by a
  test against a real container, so an upgrade that changes it fails loudly.

## 12. Glossary

| Term | Meaning |
|---|---|
| Identifier | Logical name of an API, used as the configuration key |
| Aliveness level | Depth of a liveness probe: reachable, healthy, authenticated |
| Sender | The `IHttpSender` that decides how a request travels |
| Warmup status | A `404` or `503` from a host that is still starting |
| Observation window | The part of a stub's request log that counts as this run's, opened by a reset |
| Watermark | The newest stub-clock timestamp already in the log when the window opened |
