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

### P1 verification (2026-10-16)

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

**Not executed in P1**
- Manual browser testing of the React conflict flow (only the TypeScript build was verified).
- Client-disconnect behaviour against a running server (covered by unit tests only).

### P2 – Automated integration tests and CI

**Integration tests (`tests/QueryForge.Api.IntegrationTests`)**
- `WebApplicationFactory<Program>` hosts the real HTTP pipeline (middleware, routing, model binding, JSON, ProblemDetails).
- One SQL Server 2022 container per run via Testcontainers, migrated with the real EF Core migrations; data is cleared before every test; the container is removed afterwards.
- `Testing` environment: no user secrets, Swagger, or local CORS; Redis disabled.
- 26 tests covering CRUD, normalization, `Location` header, validation (per-field errors, malformed JSON, malformed/missing `rowVersion`), 404s for GET/PUT/DELETE, duplicate slugs on create and update, pagination (order, totals, normalization, search, status filter, empty), optimistic concurrency (two-writer race, retry with latest version, stale delete, update after delete), health endpoints, and ProblemDetails shape (`application/problem+json`, `status`, `title`, `detail`, `errors`, `code`).
- New test-only packages: `Microsoft.AspNetCore.Mvc.Testing` 8.0.31 and `Testcontainers.MsSql` 4.15.0.
- `public partial class Program;` added to `Program.cs` so tests can reference the entry point.

**CI (`.github/workflows/ci.yml`)**
- .NET job: restore, Release build, all tests (TRX artifacts uploaded even on failure). Frontend job: `npm ci`, `npm run build`.
- Read-only permissions, no secrets, newer runs cancel older runs on the same ref.

**Repository quality**
- `global.json`: .NET SDK 8.0.400 with `rollForward: latestFeature`.
- `Directory.Build.props`: warnings as errors and deterministic builds only when `CI=true`.
- `.editorconfig`: formatting plus existing C# conventions as suggestions (never build-breaking).
- `.gitattributes`: LF normalization in the repository (`*.sln` CRLF, `*.yml`/`*.sh` LF).

### P2 verification (2026-10-16)

| Check | Result |
|---|---|
| `dotnet build` with `CI=true`, Release (warnings as errors) | Passed, 0 warnings |
| Application unit tests (15) | Passed — no Docker needed |
| API unit tests (10) | Passed — no Docker needed |
| Integration tests (26) against Testcontainers SQL Server | Passed — **requires Docker** |
| Mutation check: version check disabled in repository | 3 of 4 concurrency tests failed as expected; code restored |
| Integration tests with Docker unavailable | Fail fast (26 failed in < 1 s), not silently skipped |
| Unit tests only (`--filter "FullyQualifiedName!~IntegrationTests"`) | 25 passed |
| `npm ci` + `npm run build` | Passed |
| NuGet vulnerability scan (all 7 projects) | No vulnerable packages |
| Testcontainers SQL container removed after run | Yes |

**Not executed in P2**
- The GitHub Actions workflow itself: there is no GitHub remote yet. YAML syntax was not machine-validated (no YAML parser or `actionlint` available locally); it runs on the first push.
- Redis behaviour in integration tests (Redis is disabled in the test host).

## Known limitations
- No authentication or authorization.
- GitHub Actions workflow not yet executed (no remote); actions are pinned to major versions, not commit SHAs.
- Integration tests need Docker and about 2 GB of RAM for the SQL Server container.
- Redis is not covered by integration tests and is not yet used for business caching.
- While Redis is down, cache operations wait for StackExchange.Redis timeouts (~5 s); acceptable while nothing uses the cache.
- The health response is anonymous and lists dependency names; restrict it before any public deployment.
- Search does not escape `%`/`_` wildcards (parameterized, so not an injection risk).
- Frontend is a single `App.tsx` component without routing or tests.
- Docker images use floating tags (`2022-latest`, `redis:7-alpine`); the SQL Server container healthcheck only probes the TCP port.
- `QueryForge.Api.http` still targets the template `/weatherforecast` endpoint.

## Next steps
1. Push to GitHub and confirm the CI workflow passes; consider pinning actions to commit SHAs.
2. Authentication and authorization (JWT, role-based policies).
3. API Endpoints module (endpoint catalogue per API definition).
