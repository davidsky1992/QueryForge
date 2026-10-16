# QueryForge Project Status

Last updated: 2026-10-16

## Completed

### P0 – Security and repository hygiene
- Database credentials removed from tracked configuration (`appsettings*.json`, `docker-compose.yml`).
- Local secrets: SQL Server password in git-ignored `.env`; API connection string in .NET user secrets (`UserSecretsId` on `QueryForge.Api`).
- `.env.example` contains placeholders only; the initial commit was amended so no real password is in history.
- `.gitignore` covers secrets, build output, IDE/user files, and logs.
- Vulnerable transitive test dependencies (`System.Net.Http` 4.3.0, `System.Text.RegularExpressions` 4.3.0) removed by upgrading the xUnit stack.

### P1 – Optimistic concurrency and reliable health checks

**Optimistic concurrency**
- `PUT /api/api-definitions/{id}` requires `rowVersion` in the body; `DELETE` requires a `?rowVersion=` query parameter.
- The client's version is applied as EF Core's original value, so the check happens atomically in the `UPDATE`/`DELETE ... WHERE RowVersion = @original` statement.
- Stale versions return `409 Conflict` with `"code": "concurrency_conflict"`; missing or malformed versions return `400`.
- Persistence exceptions are translated to `ConcurrencyConflictException` so the application layer stays independent of EF Core.
- Frontend sends `rowVersion` on update/delete and, on conflict, discards the stale edit, reloads, and explains why.

**Health checks**
- `GET /health` reports SQL Server and Redis independently, with a 3-second timeout per check.
  - SQL Server failure → `Unhealthy` (HTTP 503). Redis failure → `Degraded` (HTTP 200).
- `GET /health/live` is a dependency-free liveness probe.
- The SQL check uses a dedicated unpooled connection with a 2-second connect timeout, so it respects the timeout and reports recovery immediately.
- Redis uses one shared `IConnectionMultiplexer` for both the distributed cache and the health check; the API starts even when Redis is down.
- Responses contain only status, fixed descriptions, and durations — never exception messages or connection details.

**Exception handling**
- Exceptions are no longer rethrown with `throw exception;`; once a response has started they propagate unchanged with their original stack trace.
- Client disconnects (`OperationCanceledException` with `RequestAborted` signalled) are logged at Debug level with status 499, not as server errors.
- Concurrency conflicts that reach the middleware are logged as warnings and return 409.
- ProblemDetails from the middleware now use camelCase JSON, matching controller responses and the frontend's error parsing.

## Verification (2026-10-16)

| Check | Result |
|---|---|
| `dotnet build` | Passed, 0 warnings |
| `dotnet test` – Application (15 tests) | Passed |
| `dotnet test` – API (10 tests) | Passed |
| `npm run build` | Passed |
| NuGet vulnerability scan (all 6 projects) | No vulnerable packages |
| Live: create → update (current version) → update (stale) → delete (stale) → delete (current) against SQL Server container | 201 → 200 → 409 → 409 → 204; missing version → 400 |
| Live: `/health` with both dependencies up | Healthy, 200 |
| Live: `/health` with Redis stopped | Degraded, 200, ~3 s |
| Live: `/health` with SQL Server stopped | Unhealthy, 503, ~3 s |
| Live: recovery after SQL Server restart | Healthy once SQL Server finished starting (~29 s) |

**Not executed**
- Manual browser testing of the React conflict flow (only the TypeScript build was verified).
- Automated integration tests against real containers (all live checks above were manual, scripted with `curl`).
- Client-disconnect behaviour against a running server (covered by unit tests only).

## Known limitations
- No authentication or authorization.
- No automated integration tests (`WebApplicationFactory` + Testcontainers) or CI pipeline.
- Redis is not yet used for business caching.
- While Redis is down, cache operations wait for StackExchange.Redis timeouts (~5 s); acceptable while nothing uses the cache.
- The health response is anonymous and lists dependency names; restrict it before any public deployment.
- Search does not escape `%`/`_` wildcards (parameterized, so not an injection risk).
- Frontend is a single `App.tsx` component without routing or tests.
- Docker images use floating tags (`2022-latest`, `redis:7-alpine`); the SQL Server container healthcheck only probes the TCP port.
- `QueryForge.Api.http` still targets the template `/weatherforecast` endpoint.

## Next steps
1. Integration tests and CI (`WebApplicationFactory`, Testcontainers, GitHub Actions, `Directory.Build.props`).
2. Authentication and authorization (JWT, role-based policies).
3. API Endpoints module (endpoint catalogue per API definition).
