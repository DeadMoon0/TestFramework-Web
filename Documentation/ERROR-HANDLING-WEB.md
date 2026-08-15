# Error Handling In TestFramework.Web

Every exception in this package derives from `TimelineFrameworkException` and carries a friendly
message, recovery steps and — where a set of valid values exists — the available options.

The design rule: a failure message must be enough to fix the problem without re-running the test.

## What Is And Is Not An Error

| Situation | Behaviour |
|---|---|
| `404`, `401`, `500` response | **Not an error.** Returned as `HttpResponseContext` for the test to assert on. |
| Connection refused, DNS failure, transport timeout | `ApiRequestFailedException` |
| Missing or unparsable configuration | `ApiConfigurationValidationException` |
| Unsubstituted `{token}` in the path | `ApiConfigurationValidationException` |
| Authentication mode configured without its values | `ApiConfigurationValidationException` |
| Asserted status did not match | `ValueAssertionException` from the framework's own assertions |
| Body cannot be read as the requested type | `ApiResponseFormatException` |
| Liveness probe answered, but not acceptably | `ApiLivenessProbeException` |

This split is deliberate. Tests routinely assert error responses, so a `404` must not abort the run.
Anything that prevents a response from existing at all does.

## ApiConfigurationValidationException

**Missing identifier.** Lists the identifiers that *are* registered, which turns a typo into a
one-second fix:

```
No API configuration was registered for identifier 'invoices'.

Recovery:
  - Add an 'Api:invoices:BaseUrl' entry to the configuration used by this run.
  - Call LoadWebConfig() on the ConfigInstance builder so the Api section is loaded.
  - When an environment provides the API, make sure SetEnv(...) runs before the step.

Available:
  - orders
  - shipping
```

**Unusable value.** Names the exact configuration key and why the value fails, including the set of
valid values for enumerations.

**Unsubstituted route token.** Raised before the request leaves the process, because sending a
literal `{id}` to a server produces a far more confusing `404` later.

## ApiRequestFailedException

Names the method, the absolute URI, the elapsed time and the underlying transport exception, then
adapts its recovery hints to the cause: timeouts point at `RequestTimeout` and `.WithTimeOut(...)`,
HTTPS failures point at `AllowInvalidCertificates`.

## Assertion Failures

Assertions run through the framework's own fluent assertions, so a failure is signalled to the
debugging UI, honours `run.AssertionScope()` and throws the Core assertion exception types. The
diagnostic context comes from how a response renders itself: a one-line summary naming the request,
the status, the duration and the first correlation header present, plus a bounded body excerpt when
the status was not successful.

```
'create' status of POST http://localhost:5080/api/items -> 500 InternalServerError in 0:00:01.2 x-correlation-id=corr-42: Be(Created) failed - expected Created, was InternalServerError
```

Correlation headers recognised for the summary: `x-correlation-id`, `x-request-id`, `traceparent`,
`request-id`.

## ApiResponseFormatException

Carries the target type, the status code, the content type and a body excerpt. Its first recovery
step is the usual root cause: the status was never asserted, and an error payload is being read with
the success schema.

## ApiLivenessProbeException

Distinguishes the two common causes. A `404` points at `HealthPath` or suggests dropping to
`Reachable`; a `401` or `403` tells you whether to probe at `Authenticated` level or expose an
anonymous health endpoint.

## SqlSchemaGenerationException

Raised while generating table definitions from a model map, never at run time. It names the property
that could not be described and what to declare instead: an explicit column type, a length, a
precision, or a hand-written script. Generation refuses to emit a column it cannot describe
faithfully rather than produce a table that silently differs from the model.

## StubConfigurationValidationException and StubAdminException

`StubConfigurationValidationException` follows the same shape as its API and SQL siblings: an
identifier with no registered configuration, listing the identifiers that are registered.

`StubAdminException` is raised when a stub's administration surface cannot be read. It is
deliberately distinct from an assertion failure: it means the assertions could not be evaluated, not
that they failed. A stub whose container has exited takes its request log with it, and that must not
read as "the call was never made".

## Secrets

`ApiKey`, `BearerToken` and `Password` never appear in messages, logs or debug values. The
`Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie` and common key headers are redacted.
Extend the policy in configuration, not in code:

```jsonc
{ "Web": { "SensitiveHeaders": [ "x-tenant-secret", "x-signature" ] } }
```

`.RedactHeaders(...)` on the config builder exists for names that are only known at run time. There
is no global mutable list, so one test project cannot change what another one redacts.

## What Callers Should Not Have To Handle

Some conditions are absorbed on purpose, because making them the caller's problem would push
infrastructure quirks into test code:

- **Warmup statuses from local hosts — in the liveness probe only.** `WebExt.Api.IsLive(...)` retries
  a `404` or `503` from a loopback or `host.docker.internal` host for a bounded window while the
  route table comes up, logging one line for the whole wait. Tune or disable it with
  `.ConfigureApiTrigger(c => c with { LocalWarmupRetryDuration = ... })`. An ordinary call is sent
  exactly once: a status code is a result, and a timeline asserting `NotFound` must not pay a
  ten-second retry for it. Put an `IsLive` step in front of a host that may still be starting.
- **Transport timeouts versus step timeouts.** With no `RequestTimeout` configured, the step timeout
  is the single source of truth, so two timeout knobs cannot silently disagree.
