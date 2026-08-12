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
`HttpResponseContext` (the step result), `IHttpSender`, `HttpHeaderRedaction`.

**`Builder/`** — two staged interfaces, one action interface per capability, implemented by
`RemoteApiBuilder`. `Call()` materializes the step.

**`Trigger/`** — `HttpApiTrigger` and `IsLive/ApiIsLiveTrigger`, plus `ApiTriggerConfig` for
warmup-retry tuning.

**`Auth/`** — the mode enum, the provider interface, the configuration-driven provider, a delegate
provider, and a variable-backed bearer provider resolved against the store at execution time.

**`Runtime/`** — `IWebComponentFactory` and the default implementation pooling `HttpClient`
instances per identifier and per connection-relevant setting.

## 6. Runtime View

A single API step:

1. `ComposedRequestVariable.GetValue` resolves every variable into an `HttpRequestSpec`.
2. `ApiConfigResolver` resolves the `ApiConfig`, failing with the registered identifiers listed.
3. `HttpRequestSpec.ResolveUri` composes the absolute URI, substituting and escaping route values
   and the query, and rejects unsubstituted tokens.
4. `IWebComponentFactory.CreateSender` returns a pooled sender for that identifier.
5. Authentication is applied — the per-request override wins over the configured mode.
6. The request is sent; warmup statuses from local hosts are retried within a bounded window.
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
envelope.

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
| Warmup retry limited to local hosts | A transient `404` while a local route table warms up is infrastructure noise. On a remote host the same status is a real answer and must not be hidden. |
| Pooled clients keyed by connection-relevant settings | Connection reuse across steps, while a run that rewrites the endpoint still gets a fresh client. |

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

## 12. Glossary

| Term | Meaning |
|---|---|
| Identifier | Logical name of an API, used as the configuration key |
| Aliveness level | Depth of a liveness probe: reachable, healthy, authenticated |
| Sender | The `IHttpSender` that decides how a request travels |
| Warmup status | A `404` or `503` from a host that is still starting |
