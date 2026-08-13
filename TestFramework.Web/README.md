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

## SQL Server

Configure a database the same way an API is configured, then assert what the API actually did to it.

```jsonc
{
  "Sql": {
    "main": { "Server": "localhost,1433", "Database": "SampleDb", "IntegratedSecurity": true, "TrustServerCertificate": true }
  }
}
```

Tell the framework where a model lives. Explicit registration wins, then `[Table]`/`[Column]`/`[Key]`
attributes, then convention (type name is the table, `Id` is the key):

```csharp
ConfigInstance config = ConfigInstance.FromJsonFile("local.testsettings.json")
    .LoadWebConfig()
    .AddWebSqlModels(models => models.For<Order>().Schema("sales").Table("Orders").Key(x => x.Id).Generated(x => x.Id))
    .Build();
```

A row has a key and a lifecycle, so it is an artifact. A statement that changes data is an action, so
it is a step. An aggregate is neither, so it is an observation:

```csharp
Timeline timeline = Timeline.Create()
    .SetupArtifact("customer")                                   // seeded, then removed on teardown
    .SetVariable("orderName", Var.Const("Testauftrag"))
    .Trigger(WebExt.Api.Http("orders").Post("api/orders").WithJsonBody(Var.Ref<CreateOrder>("payload")).Call()).Name("create")
    .FindArtifact("order", WebExt.ArtifactFinder.Sql.Where<Order>("main", "Name = @name")
        .WithParameter("name", Var.Ref<string>("orderName")))
    .Build();

TimelineRun run = await timeline.SetupRun(config)
    .AddArtifact("customer",
        WebExt.Artifact.Sql.Row<Customer>("main", Var.Const("4711")),
        new SqlRowArtifactData<Customer>(new Customer { Id = 4711, Name = "Testkunde" }))
    .RunAsync();

run.ApiStatus("create").Should().Be(HttpStatusCode.Created);
run.SqlRow<Order>("order").Select(order => order.Quantity).Should().Be(3);
```

| Surface | Use |
|---|---|
| `WebExt.Artifact.Sql.Row<T>(id, keys...)` | a row the test owns: upserted on setup, deleted on teardown |
| `WebExt.ArtifactFinder.Sql.Where<T>(id, "...")` | locate rows; a predicate that matches nothing fails the step |
| `WebExt.Sql.Execute(id, "...")` | `UPDATE` / `DELETE` |
| `WebExt.Sql.Script(id, SqlScript.FromFile("seed.sql"))` | seeding, batch-aware (`GO` splits batches) |
| `WebExt.Sql.Scalar<T>(id, "...")` | counts and aggregates |
| `WebExt.Sql.IsLive(id, SqlAlivenessLevel.Database)` | wait for a database to answer |

Parameters are variable-backed, like request bodies:

```csharp
WebExt.Sql.Execute("main", "UPDATE Orders SET Status = @status WHERE Id = @id")
    .WithParameter("status", Var.Const(9))
    .WithParameter("id", Var.Ref<string>("orderId"))
```

Two rules worth knowing:

- **Rows the test seeds are deleted on teardown; rows it merely finds are not.** A test must never
  delete data the application under test created.
- **Setup upserts.** A rerun against a database a previous run left dirty converges instead of
  failing on a duplicate key.

### Generating a fixture schema

A table the test owns can be derived from the models it already declares, instead of from a script
kept in step with them by hand:

```csharp
Timeline timeline = Timeline.Create()
    .Trigger(WebExt.Sql.Script("main", SqlSchema.CreateTablesScript(typeof(Order), typeof(Customer)))).Name("schema")
    .Build();
```

The map supplies the table, columns, key and which columns the database assigns. What a CLR type
cannot supply is declared alongside it:

```csharp
.AddWebSqlModels(models => models.For<Order>()
    .Schema("sales").Table("Orders")
    .Key(x => x.Id).Identity(x => x.Id)     // IDENTITY(1,1)
    .MaxLength(x => x.Name, 200)            // NVARCHAR(200) instead of NVARCHAR(MAX)
    .Required(x => x.Name)                  // overrides what the property type implies
    .Precision(x => x.Total, 18, 2)         // DECIMAL(18,2)
    .ColumnType(x => x.Amount, "money"))    // verbatim, for anything not inferable
```

`[MaxLength]`, `[StringLength]`, `[Required]`, `[DatabaseGenerated]` and `[Column(TypeName = "...")]`
say the same things as attributes.

Generation covers schemas, tables, columns, nullability, identities and primary keys — **not** foreign
keys, indexes, check constraints or collations. It is scaffolding for a database the test owns, not a
migration tool: a table generated from test-side models proves only that the models agree with
themselves. Where the real schema is owned elsewhere, point the test at that schema instead.

Credentials can come from configuration or from an `ISqlCredentialProvider`, so one settings file can
run with integrated security locally and a SQL login elsewhere. Passwords and connection strings
never reach a log. Parameter *names* are logged; values only when you ask via
`.ConfigureSqlSteps(c => c with { LogParameterValues = true })`.

## Stubbed Dependencies

An application does two things: it answers, and it calls other systems. A response body shows the
first half only. Declare what a dependency answers, then assert on what was actually sent to it.

```jsonc
{ "Stub": { "payments": { "BaseUrl": "http://localhost:9091/" } } }
```

```csharp
internal sealed class PaymentsStubDefinition : StubDefinition
{
    public override StubIdentifier Identifier => "payments";

    protected override void Configure(StubMappingBuilder builder) => builder
        .OnGet("/api/rates/EUR")
            .RespondJson(HttpStatusCode.OK, new { currency = "EUR", rate = 1.08 })
        .OnPost("/api/charges")
            .WithHeader("Idempotency-Key")
            .WithBodyContaining("\"amount\"")
            .RespondJson(HttpStatusCode.Created, new { id = "{{Random Type=Guid}}" }, useTemplating: true);
}
```

Mappings are tried in declaration order, so declare the specific case before the general one. They
are plain data — no delegates — because the server that runs them may be in another process
entirely; `{{request.body.amount}}` templating covers what a callback would otherwise be for.

```csharp
Timeline timeline = Timeline.Create()
    .Trigger(WebExt.Stub.Reset("payments")).Name("clean")
    .Trigger(WebExt.Api.Http("orders").Post("api/orders").WithJsonBody(...).Call()).Name("create")
    .WaitForEvent(WebExt.Stub.Called("payments", HttpMethod.Post, "/api/charges"))
        .WithTimeOut(TimeSpan.FromSeconds(30)).Name("charged")
    .Trigger(WebExt.Stub.Calls("payments")).Name("calls")
    .Build();

run.StubCall("charged").Select(call => call.Body).Should().Contain("\"amount\":30");
run.StubCalls("calls").Should().HaveCount(1);
run.StubUnmatchedCalls("calls").Should().HaveCount(0);
```

That last line is the one worth adopting: an unmatched call is the application asking a dependency
for something the test never declared, and nothing else in a test would reveal it.

Verification reads the server's own request log over its admin surface, so it works against a stub
this run started, one your team runs permanently, or one you started by hand. **This package declares
and verifies; it does not host.** `TestFramework.Container.Web` hosts declarations in a container.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `No API configuration was registered for identifier 'x'` | The `Api:x` section is missing, or `LoadWebConfig()` was not called. The message lists the identifiers that *are* registered. |
| `the path '...' still contains an unsubstituted route token` | A `{token}` in the path has no `WithRouteValue`. |
| `API 'x' did not answer ...` | Transport failure. The message names the authority to check, and suggests the timeout or certificate setting when those are the likely cause. |
| `401` or `403` against a Windows-authenticated API | Set `Auth` to `Negotiate` for that identifier. |
| `No SQL configuration was registered for identifier 'x'` | The `Sql:x` section is missing. The message lists the identifiers that *are* registered. |
| `No key column could be determined for 'T'` | Register the model, annotate the key with `[Key]`, or name it `Id`. |
| A local host answers `404` right after start | Already handled: warmup statuses from loopback hosts are retried for a bounded window. Tune with `.ConfigureApiTrigger(...)`. |
| Need to see what headers actually went out | `.ConfigureApiTrigger(c => c with { LogRequestHeaders = true })`, with sensitive values redacted. |
| `The response body could not be read as 'T'` | Assert the status first; error responses rarely use the success schema. |
| `No stub configuration was registered for identifier 'x'` | The `Stub:x` section is missing, or no environment published it. |
| `The stub at '...' did not answer` | The stub server is gone. A container that exited takes its request log with it. |
| A stub assertion sees no calls | Check `StubUnmatchedCalls` first: the application may have called a path no mapping covers. |

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

This package covers reaching an API, a database and a stub that are already running: it does not
start or host any of them. The `IHttpSender` seam exists so that adding a hosting mode later does not
change the timelines written against it.
