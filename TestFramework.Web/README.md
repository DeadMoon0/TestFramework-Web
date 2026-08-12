![Icon](https://raw.githubusercontent.com/DeadMoon0/TestFramework-Common/96ef4240c1e55ba95a20b99285219a61407c6355/Assets/Icon.svg)

# TestFramework.Web

`TestFramework.Web` drives REST APIs from inside a TestFramework timeline.

A request is a step. The response is a step result. Paths, bodies, headers and query values are all
variables, so one timeline runs many times with different data — and the same timeline works against
a deployed API today and against a locally hosted or containerized one later, because only the
environment changes.

## Install

```bash
dotnet add package TestFramework.Web
```

Targets `net8.0` and `net10.0`.

## Quickstart

Configure the API once, by identifier:

```jsonc
{
  "Api": {
    "sample": {
      "BaseUrl": "http://localhost:5080/",
      "HealthPath": "/health",
      "Auth": "None"
    }
  }
}
```

Then write the timeline:

```csharp
ConfigInstance config = ConfigInstance.FromJsonFile("local.testsettings.json")
    .LoadWebConfig()
    .Build();

Timeline timeline = Timeline.Create()
    .SetVariable("itemId", Var.Const("2"))
    .Trigger(WebExt.Api.IsLive("sample", ApiAlivenessLevel.Healthy)).Name("live")
    .Trigger(WebExt.Api.Http("sample")
        .Get("api/items/{id}")
        .WithRouteValue("id", Var.Ref<string>("itemId"))
        .Call()).Name("get-item")
    .Build();

TimelineRun run = await timeline.SetupRun(config).RunAsync();
run.EnsureRanToCompletion();

run.ApiStatus("get-item").Should().Be(HttpStatusCode.OK);
run.ApiJson<SampleItem>("get-item").Select(item => item.Id).Should().Be("2");
```

## Conceptual Model

| Concept | What it is |
|---|---|
| **Identifier** | The logical name of an API. Timelines reference the identifier, never a URL. |
| **`ApiConfig`** | Everything needed to reach one identifier: base URL, health path, auth, timeout. |
| **`WebExt.Api.Http(...)`** | A two-stage builder: choose method and path, then shape and `Call()`. |
| **`HttpResponseContext`** | The step result. Plain data, so it survives the debugging UI transport. |
| **`IHttpSender`** | The seam that decides *how* a request travels. Swapped by hosting environments. |
| **`web.restapi`** | The environment requirement kind a step declares. |

### Status codes are results, not failures

An unsuccessful status code is returned to the timeline so you can assert on it:

```csharp
run.ApiStatus("missing").Should().Be(HttpStatusCode.NotFound);
```

Only transport problems — connection refused, DNS failure, timeout — raise
`ApiRequestFailedException`.

## Assertions

Assertions go through the framework's own fluent assertions, so they are signalled to the debugging
UI, they participate in `run.AssertionScope()`, and they fail with the framework exception types:

```csharp
run.ApiStatus("create").Should().Be(HttpStatusCode.Created);
run.ApiHeader("create", "Location").Should().StartWith("/api/items/");
run.ApiBody("list").Should().Contain("widget");
run.ApiJson<SampleItem[]>("list").Should().HaveCount(3);
run.ApiJson<SampleItem>("get-item").Select(item => item.Id).Should().Be("2");
run.ApiProbe("live").Select(probe => probe.Success).Should().Be(true);
```

Collect several failures instead of stopping at the first:

```csharp
using (run.AssertionScope())
{
    run.ApiStatus("create").Should().Be(HttpStatusCode.Created);
    run.ApiHeader("create", "Location").Should().StartWith("/api/items/");
}
```

Step state uses the Core asserters unchanged:

```csharp
run.Step("create").Should().HaveCompleted();
run.Step("dead").Should().HaveThrown<ApiRequestFailedException>();
```

A failing assertion names the call that produced it, because the response renders as a one-line
summary:

```
'create' status of POST http://localhost:5080/api/items -> 500 InternalServerError in 0:00:01.2 x-correlation-id=corr-42: Be(Created) failed — expected Created, was InternalServerError
```

### The path is the contract

Endpoints are addressed by path, never derived from the API's own controller types. A route change on
the server is supposed to break the test — that is the test's job. It also means the test project
never references the application project, so the API stays a black box.

## Composing Requests

```csharp
WebExt.Api.Http("sample")
    .Post("api/items")
    .WithJsonBody(Var.Ref<CreateItem>("payload"))
    .WithHeader(Var.Const("x-correlation-id"), Var.Ref<string>("correlationId"))
    .WithQuery("dryRun", Var.Const("false"))
    .Call();
```

- `WithRouteValue` substitutes `{token}` in the path and escapes the value
- `WithQuery` escapes keys and values, and omits a parameter whose variable resolves to null
- `WithJsonBody` serializes with web naming rules and sets the JSON content type
- `WithBody` takes text or bytes with an explicit content type
- A leading slash on the path never discards a base path: `BaseUrl` of `http://host/root/` plus
  `/api/items` resolves to `http://host/root/api/items`

## Authentication

Set the mode per identifier:

```jsonc
{ "Api": { "sample": { "BaseUrl": "http://host/", "Auth": "Negotiate" } } }
```

| Mode | Behaviour |
|---|---|
| `None` | Nothing is added. |
| `ApiKey` | Sends `ApiKey` in the header named by `ApiKeyHeaderName`. |
| `Bearer` | Sends `BearerToken` as an `Authorization: Bearer` header. |
| `Basic` | Sends `UserName` and `Password` as HTTP basic. |
| `Negotiate` | Uses the current process credentials. |

`Negotiate` is the mode for APIs behind Windows integrated authentication: it needs no change on the
API side, because the credentials ride on the handler rather than the message.

Per-request overrides win over configuration, which is how a token produced by an earlier step
authenticates a later one:

```csharp
.WithBearerToken(Var.Ref<string>("token"))
.WithAuth(new DelegateApiAuthenticationProvider(async (message, ct) => { /* custom flow */ }))
```

Credential values are never written to logs or debug output. `Authorization`,
`Proxy-Authorization`, `Cookie`, `Set-Cookie` and common key headers are always redacted; extend the
policy in configuration rather than in code:

```jsonc
{
  "Web": { "SensitiveHeaders": [ "x-tenant-secret", "x-signature" ] }
}
```

A comma-separated string works too. For names only known at run time there is
`.RedactHeaders("x-runtime-secret")` on the config builder.

## Liveness

```csharp
WebExt.Api.IsLive("sample", ApiAlivenessLevel.Reachable)   // the host answers at all
WebExt.Api.IsLive("sample", ApiAlivenessLevel.Healthy)     // HealthPath returns 2xx
WebExt.Api.IsLive("sample", ApiAlivenessLevel.Authenticated) // HealthPath accepts the credentials
```

`Reachable` accepts any status code on purpose — it proves the socket is open without assuming
anything about routes or authorization, which is what you want while waiting for a host to boot.
Combine it with the normal timeline modifiers:

```csharp
.Trigger(WebExt.Api.IsLive("sample")).WithTimeOut(TimeSpan.FromMinutes(1)).WithRetry(10, CalcDelays.Fixed(TimeSpan.FromSeconds(2)))
```

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `No API configuration was registered for identifier 'x'` | The `Api:x` section is missing, or `LoadWebConfig()` was not called. The message lists the identifiers that *are* registered. |
| `the path '...' still contains an unsubstituted route token` | A `{token}` in the path has no `WithRouteValue`. |
| `API 'x' did not answer ...` | Transport failure. The message names the authority to check, and suggests the timeout or certificate setting when those are the likely cause. |
| `401` or `403` against a Windows-authenticated API | Set `Auth` to `Negotiate` for that identifier. |
| A local host answers `404` right after start | Already handled: warmup statuses from loopback hosts are retried for a bounded window. Tune with `.ConfigureApiTrigger(...)`. |
| Need to see what headers actually went out | `.ConfigureApiTrigger(c => c with { LogRequestHeaders = true })`, with sensitive values redacted. |
| `The response body could not be read as 'T'` | Assert the status first; error responses rarely use the success schema. |

## Tuning

```csharp
ConfigInstance config = ConfigInstance.FromJsonFile("local.testsettings.json")
    .LoadWebConfig()
    .ConfigureApiTrigger(c => c with
    {
        LocalWarmupRetryDuration = TimeSpan.FromSeconds(30),
        LogRequestHeaders = true,
    })
    .Build();
```

Values you do not mention keep their defaults.

## Scope

This package covers calling REST APIs that are already reachable. It does not start or host the
application under test, and it does not assert on databases. The `IHttpSender` seam exists so that
adding a hosting mode later does not change the timelines written against it.
