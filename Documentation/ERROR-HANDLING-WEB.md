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
| Asserted status did not match | `ApiStatusAssertionException` |
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

## ApiStatusAssertionException

Raised by `ExpectStatus`, `ExpectSuccess` and `ExpectJson`. The message carries the request, the
actual status, the elapsed time, any correlation headers (`x-correlation-id`, `x-request-id`,
`traceparent`, `request-id`) and a body excerpt bounded to 2 KB:

```
Expected 200 OK but the API answered 500 InternalServerError.
Request: POST http://localhost:5080/api/items
Elapsed: 0:00:01.24
x-correlation-id: corr-42
Body: {"error":"boom"}

Recovery:
  - Look the request up in the API log using the correlation header above.
  - A 5xx is a server-side fault: the API log, not the test, holds the cause.
```

Recovery adapts to the status: `401`/`403` point at the `Auth` mode and name `Negotiate`, `404`
points at path composition, `5xx` says plainly that the cause is server-side.

## ApiResponseFormatException

Carries the target type, the status code, the content type and a body excerpt. Its first recovery
step is the usual root cause: the status was never asserted, and an error payload is being read with
the success schema.

## ApiLivenessProbeException

Distinguishes the two common causes. A `404` points at `HealthPath` or suggests dropping to
`Reachable`; a `401` or `403` tells you whether to probe at `Authenticated` level or expose an
anonymous health endpoint.

## Secrets

`ApiKey`, `BearerToken` and `Password` never appear in messages, logs or debug values. The
`Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie` and common key headers are redacted.
Register additional names with `HttpHeaderRedaction.AddSensitiveHeader(...)`.

## What Callers Should Not Have To Handle

Some conditions are absorbed on purpose, because making them the caller's problem would push
infrastructure quirks into test code:

- **Warmup statuses from local hosts.** A `404` or `503` from a loopback or `host.docker.internal`
  host is retried for a bounded window while the route table comes up. Tune or disable it through
  `ApiTriggerConfig`.
- **Transport timeouts versus step timeouts.** With no `RequestTimeout` configured, the step timeout
  is the single source of truth, so two timeout knobs cannot silently disagree.
