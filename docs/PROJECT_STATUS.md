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
- The GitHub Actions workflow itself (no GitHub remote at the time). It has since run successfully — see [Continuous integration](#continuous-integration).
- Redis behaviour in integration tests (Redis is disabled in the test host).

### P3 – Dockerization and local deployment

**Images**
- `src/QueryForge.Api/Dockerfile` (context: repository root), multi-stage on `dotnet/sdk:8.0` (SDK 8.0.426, satisfies `global.json`):
  - `api` target (default): Release publish on `dotnet/aspnet:8.0` (Debian 12), non-root `app` user (UID 1654), port 8080, `curl` for the container health check. ~356 MB.
  - `migrator` target: EF Core migration bundle (`dotnet-ef` 8.0.11, matching EF Core) on `dotnet/aspnet:8.0`, non-root. The connection string is supplied at runtime only. ~371 MB.
  - Restore layer cached from project files only; root `.dockerignore` excludes `.env*`, secrets, build/test output, tests, docs, the frontend, and Git/IDE metadata.
- `src/QueryForge.Web/Dockerfile` (context: `src/QueryForge.Web`): `npm ci` + `npm run build` on `node:20-alpine`, served by non-root `nginxinc/nginx-unprivileged:1.27-alpine` on port 8080. ~74 MB.
  - nginx (templated config) serves the SPA with fallback routing, long-lived caching for hashed assets, `no-cache` for `index.html`, and basic security headers.
  - Same-origin reverse proxy: `/api/*` and `/health*` are forwarded to `API_UPSTREAM` (default `http://api:8080`), resolved per request through Docker DNS so API restarts are followed.
  - Built with an empty `VITE_API_BASE_URL`, so the bundle uses relative URLs and contains no hostnames or secrets.

**Docker Compose (`docker-compose.yml`)**
- Services: `sqlserver`, `redis`, `migrator`, `api`, `web`; project name `queryforge`; `container_name` removed.
- Dependency chain with conditions: `sqlserver` healthy → `migrator` completed successfully → `api` healthy (also waits for `redis`) → `web`.
- SQL Server health check now logs in and runs `SELECT 1` with `sqlcmd` (password read inside the container via `SQLCMDPASSWORD`), replacing the TCP-only probe.
- Containers use service names (`sqlserver,1433`, `redis:6379`, `api:8080`); credentials come only from `.env`.
- All host ports bound to `127.0.0.1` (previously SQL Server and Redis listened on all interfaces) and configurable via `.env`.
- Existing named volumes kept, so data from earlier setups carries over.
- Memory limits for an 8 GB machine: SQL Server 2 GB, API 512 MB, migrator 256 MB, Redis 256 MB, web 64 MB.

**CI**: new `docker` job validates the Compose file (non-secret placeholder password) and builds all three images without pushing.

**Docs**: README covers prerequisites, configuration, full-stack and hybrid workflows, service URLs, how the frontend reaches the API, migrations, stop/restart/cleanup, troubleshooting, and a Local Compose vs Azure App Service comparison.

### P3 verification (2026-10-16)

| Check | Result |
|---|---|
| `dotnet build -c Release` with `CI=true` | Passed, 0 warnings |
| All tests in Release (15 + 10 + 26) | 51 passed |
| `npm ci` + `npm run build` | Passed |
| `docker build` API, migrator, and web images | Passed |
| Images run as non-root (`app` 1654, `nginx` 101) | Verified |
| No `.env`, `secrets.json`, or `.pfx` in images; password absent from image history | Verified |
| Frontend bundle contains no `localhost:5111` (relative URLs) | Verified |
| `docker compose config` with `.env`, and CI-style with a placeholder and no `.env` | Valid; without a password Compose fails with a clear message |
| `docker compose up --build -d` | All services healthy; migrator exited 0 |
| `/health` direct (`:5080`) and through nginx (`:8080`) | Healthy, SQL Server and Redis both Healthy |
| Swagger at `:5080/swagger/v1/swagger.json` | 200 |
| Frontend: index, SPA fallback route, asset caching, security headers | Verified |
| CRUD through nginx (`:8080/api`): create 201, get 200, update 200, stale update 409, list | Passed |
| `Location` header through the proxy | Initially lost the port (`$host`); fixed with `$http_host`, now `http://localhost:8080/...` |
| API container restart while nginx keeps running | Requests through nginx succeed after restart |
| Migrator against an empty database | Created `__EFMigrationsHistory` and `ApiDefinitions`; throwaway database dropped |
| `docker compose down` → `up` (volumes kept) | Previously created record still present |
| Test data cleanup | Smoke-test records deleted through the API |

### P3 review and re-verification (2026-10-16)

Review of the committed P3 work against the P3 requirements found and fixed:

| Issue | Fix |
|---|---|
| Base `appsettings.json` contained `Redis: localhost:6379`, an environment-specific URL that would also apply in Production | Removed; Development keeps it in `appsettings.Development.json`, Compose sets `ConnectionStrings__Redis`, Production falls back to the in-memory cache when unset |
| `appsettings.Development.json` was copied into the API image | Excluded via `.dockerignore` |
| `dotnet-ef` was installed after `COPY src/`, so every code change re-downloaded it | Installed before the source copy; verified the layer stays cached after a source change |
| `docker compose config` prints resolved credentials; undocumented | README warns to use `--quiet`; added a per-environment configuration table |
| **Regression from P3:** after binding SQL Server to `127.0.0.1` only, the Windows-run API (Option B) could not connect with `Server=localhost,1433` — SqlClient connections by the name `localhost` time out through Docker Desktop's loopback port forwarding (explicit `127.0.0.1` and `[::1]` work) | Option B now uses `127.0.0.1,1433` (README, troubleshooting) and `127.0.0.1:6379` for Redis; the local user secret was updated accordingly. Binding `[::1]` as well did not help and was not kept |

Re-verification after the fixes:

| Check | Result |
|---|---|
| `docker compose down`, QueryForge images removed, `docker compose build --no-cache` | Passed (120 s) |
| Images: non-root, no `.env`/secrets/`.pfx`, password absent from history, only base `appsettings.json` in the API image, no `localhost:5111` in the bundle | Verified |
| `docker compose up -d` from the fresh images | All services healthy; migrator exited 0 |
| `/health` direct and through nginx; Swagger; index and SPA fallback | Passed |
| CRUD through nginx incl. `Location` header, stale update 409, stale delete 409 | Passed |
| `down` → `up` persistence | Record kept; test data deleted afterwards |
| API image with `ASPNETCORE_ENVIRONMENT=Production` and no Redis setting | Healthy; Redis reports "not configured" (in-memory cache); Swagger 404 |
| Option B: `dotnet ef database update` and the Windows-run API against Compose SQL Server/Redis | Passed after the `127.0.0.1` fix; records created on the host are visible through the containerized stack |
| Release build with `CI=true` + all 51 tests | Passed |

**Not executed in P3**
- The new CI `docker` job (no GitHub remote at the time; the same commands were run locally). It has since passed on GitHub — see [Continuous integration](#continuous-integration).
- Manual browser testing of the containerized frontend (verified with HTTP requests only).
- Deployment to Azure App Service (documented, not implemented).

## Continuous integration

Repository: https://github.com/davidsky1992/QueryForge (public, default branch `master`).

### First GitHub Actions run (2026-10-16)

[Run 38882286607](https://github.com/davidsky1992/QueryForge/actions/runs/38882286607), triggered by the push of `9d19d5a`, concluded **success**:

| Job | Result | Duration | Steps |
|---|---|---|---|
| .NET build and tests | Success | 56 s | Restore, Release build (`CI=true`, warnings as errors), `dotnet test` (28 s), TRX upload — all steps succeeded |
| Docker image builds | Success | 50 s | Compose validation, API, migrator, and web image builds — all steps succeeded |
| Frontend build | Success | 14 s | `npm ci`, type-check and Vite build — all steps succeeded |

**Test counts — local vs. CI**
- **Verified locally:** 51 tests passed (15 Application unit tests, 10 API unit tests, 26 API integration tests against a SQL Server Testcontainers container), most recently in the P3 re-verification.
- **Confirmed by the GitHub run:** only that the `Test` step succeeded and a `test-results` artifact (13 KB of TRX files) was uploaded. The job logs and artifact require a signed-in GitHub account and were not inspected, so the CI test count has **not** been independently confirmed. It should not be inferred from the step duration.

The run reported two non-failing annotations, addressed below: Node.js 20 deprecation for the actions used, and the `ubuntu-latest` migration to Ubuntu 26 starting 2026-10-19.

### CI maintenance (2026-10-16)
- Actions upgraded to their current major versions, all running on Node.js 24 (verified from each official repository's `action.yml` and release notes): `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/cache@v6`, `actions/upload-artifact@v7`, `actions/setup-node@v7`. The inputs used by the workflow are unchanged in these versions.
- All jobs pinned to `runs-on: ubuntu-24.04` (the image used by the first successful run) so the Ubuntu 26 migration of `ubuntu-latest` cannot change the build environment unannounced. Trade-off: the runner must be bumped deliberately later, and GitHub retires older runner images on its own schedule, so the pin needs periodic review.
- Validated locally with `actionlint` 1.7.12 (Docker image `rhysd/actionlint:1.7.12`): 0 errors. A deliberately broken copy confirmed the linter reports invalid runner labels, values, and expressions.
- **Requires a GitHub run to confirm:** that the upgraded actions behave as before (checkout, .NET setup, NuGet cache restore/save, artifact upload, npm cache).

## Known limitations
- No authentication or authorization.
- Actions are pinned to major-version tags, not commit SHAs.
- The CI test count is not independently confirmed (logs and artifacts require sign-in); 51 tests are verified locally.
- The project still builds the frontend with Node.js 20 (`node-version: 20` in CI, `node:20-alpine` in the frontend Dockerfile), which reached end of life in April 2026. This is the project toolchain, separate from the Node.js 24 runtime used by the actions themselves.
- Base images use floating tags (`mssql/server:2022-latest`, `redis:7-alpine`, `dotnet/*:8.0`, `node:20-alpine`, `nginx-unprivileged:1.27-alpine`); pin digests for reproducible release builds.
- The API image uses the Debian-based ASP.NET runtime (with `curl` for health checks) rather than a smaller chiseled image.
- Local Compose uses the `sa` login, an unencrypted connection, and password-less Redis; acceptable on loopback only.
- Integration tests need Docker and about 2 GB of RAM for the SQL Server container.
- Redis is not covered by integration tests and is not yet used for business caching.
- While Redis is down, cache operations wait for StackExchange.Redis timeouts (~5 s); acceptable while nothing uses the cache.
- The health response is anonymous and lists dependency names; restrict it before any public deployment.
- Search does not escape `%`/`_` wildcards (parameterized, so not an injection risk).
- Frontend is a single `App.tsx` component without routing or tests.
- `QueryForge.Api.http` still targets the template `/weatherforecast` endpoint.

## Next steps
1. Push the CI maintenance commit and confirm all three jobs still pass with the upgraded actions on `ubuntu-24.04`.
2. Azure deployment: push the API image to Azure Container Registry, provision App Service, Azure SQL, Azure Cache for Redis, and Key Vault; run the migration bundle in the pipeline.
3. Authentication and authorization (JWT, role-based policies).

CI follow-ups (small, separate changes):
- Upgrade the project's Node.js toolchain from 20 to a supported LTS (CI, frontend Dockerfile, local setup).
- Publish test counts to the job summary (`$GITHUB_STEP_SUMMARY`) so they are visible on the run page.
- Evaluate Ubuntu 26 runners before moving off `ubuntu-24.04`.
- Optionally pin actions to commit SHAs (with Dependabot updates).
