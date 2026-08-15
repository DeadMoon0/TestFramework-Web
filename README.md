![Icon](https://raw.githubusercontent.com/DeadMoon0/TestFramework-Common/96ef4240c1e55ba95a20b99285219a61407c6355/Assets/Icon.svg)
[![NuGet Version](https://img.shields.io/nuget/v/TestFramework.Web?label=nuget%20TestFramework.Web)](https://www.nuget.org/packages/TestFramework.Web)

# TestFramework-Web

`TestFramework.Web` lets a normal TestFramework timeline drive web applications: the REST API at the
front and the SQL Server database behind it.

Use it when the thing under test speaks HTTP, and assert what it did to its database in the same
timeline. The timeline shape, variables, artifacts, retries, timeouts and debugging UI are the same
ones the rest of the framework uses.

## Choose Your Path

- **An API is already running somewhere** (dev box, test stage, a container you started yourself):
  configure `Api:<identifier>:BaseUrl` and write steps. Nothing else needed.
- **The API is behind Windows integrated authentication**: set `Auth` to `Negotiate`. No change on
  the API side.
- **You want to verify what the API did to its database**: configure `Sql:<identifier>` and use row
  artifacts and query finders in the same timeline.
- **You need the API or the database booted for you**: not covered by this package. Timelines are
  written against the `IHttpSender` and `ISqlExecutor` seams, so a container lane slots in without
  changing them.

## Install

```bash
dotnet add package TestFramework.Web
```

## Source Of Truth

This repository-level README is the landing page. The maintained onboarding guide, conceptual model,
authentication matrix and troubleshooting table live in
[TestFramework.Web/README.md](./TestFramework.Web/README.md).

Use this file for:
- package identity at a glance
- the shortest possible first-use path
- links into the maintained package docs

## What It Does

```csharp
Timeline timeline = Timeline.Create()
    .Trigger(WebExt.Api.IsLive("sample", ApiAlivenessLevel.Healthy)).Name("live")
    .Trigger(WebExt.Api.Http("sample").Get("api/items").Call()).Name("list")
    .Build();

TimelineRun run = await timeline.SetupRun(config).RunAsync();
run.EnsureRanToCompletion();

run.ApiStatus("list").Should().Be(HttpStatusCode.OK);
run.ApiJson<SampleItem[]>("list").Should().HaveItems();
```

Two rules shape everything else:

1. **A status code is a result, not a failure.** Non-2xx responses come back so tests can assert on
   them; only transport problems throw.
2. **The path is the contract.** Endpoints are never derived from the application's types, so the
   test project does not reference the application and a server-side route change fails the test.

Assertions use the framework's own fluent assertions, so they reach the debugging UI and honour
`run.AssertionScope()` exactly like Core assertions do.

## Current Scope

REST requests against a reachable API, liveness probes, request authentication including Windows
Negotiate, SQL Server row artifacts, query finders, statement steps, and assertions through the
framework's own fluent assertions.

The package is client side only: it talks to things that are already listening. Starting an
application or a database is the container lane's job.

## Repository Layout

| Path | Purpose |
|---|---|
| `TestFramework.Web/` | the shipped package |
| `UnitTests/TestFramework.Web.SampleApi/` | a self-contained API whose endpoints exist to exercise framework behaviour |
| `UnitTests/TestFramework.Web.Tests/` | unit tests plus integration tests over a real socket |
| `AI/` | the addon skill for AI assistants |
| `Documentation/` | architecture notes and the error-handling guide |

## Building And Testing

```bash
dotnet build TestFramework.Web.slnx -c Release
```

```bash
dotnet test UnitTests/TestFramework.Web.Tests/TestFramework.Web.Tests.csproj -c Release
```

The integration tests start the sample API on an ephemeral loopback port, so they need no Docker, no
external service and no configuration.

The SQL Server round-trip tests are the one exception, and they skip themselves rather than fail:
set `TESTFRAMEWORK_WEB_SQL` to a connection string to run them, or filter them out wholesale with
`--filter "Category!=SqlServer"`.
