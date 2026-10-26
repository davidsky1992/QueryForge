# QueryForge Project Status

Last updated: 2026-10-26

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

### P4 – Security hardening

**Audit before changes:** no authentication or rate limiting; all five API-definition endpoints and the detailed `/health` report were anonymous; no endpoint executes user-supplied SQL (the only raw SQL is the health check's fixed `SELECT 1`); CORS was hardcoded and Development-only; nginx forwarded headers were not processed by the API; the frontend had no Content-Security-Policy.

**Authentication and authorization**
- Custom ASP.NET Core authentication scheme `ApiKey` reading `X-Api-Key`. Keys are 32 random bytes; configuration holds only their SHA-256 hashes (`Security:ApiKeys:<n>:Name/Hash/Roles`). The presented key is hashed and compared with every configured hash using `CryptographicOperations.FixedTimeEquals`, without early exit. Keys over 256 characters are rejected before hashing.
- Roles: `reader` (GET API definitions) and `admin` (also POST/PUT/DELETE and detailed health). A fallback policy requires an authenticated key on every endpoint; public endpoints are explicitly allowlisted (`/health/live`, `/health/ready`, and Swagger in Development).
- `401` (with `WWW-Authenticate: ApiKey header="X-Api-Key"`) for missing or invalid keys and `403` for insufficient roles, both as ProblemDetails that never echo the key. Wrong and missing keys receive identical responses.
- Startup validation (`ValidateOnStart`) rejects malformed hashes, unknown roles, and duplicates; outside Development at least one key is required. Empty hash slots (unset Compose variables) are ignored.
- `scripts/New-QueryForgeApiKey.ps1` generates a key and prints its hash and configuration commands. Rotation is done by overlapping old and new hashes.
- No database migration: keys live in configuration.

**Health endpoints**
- `/health/live`: public, not rate limited, `{"status":"Healthy"}` only. Still used by the Compose health check.
- `/health/ready` *(new)*: public, rate limited, runs all checks but returns only the overall status (`200`/`503`).
- `/health`: detailed per-dependency report, now requires an `admin` key.

**Rate limiting** (built-in ASP.NET Core middleware, fixed one-minute windows, `429` ProblemDetails with `Retry-After`): Reads 120/min and Writes 30/min per key name; unauthenticated requests to protected endpoints 30/min per client IP, counted before the `401`; health 60/min per IP. Configurable under `RateLimiting`; invalid values stop startup. Counters are in memory per instance.

**Proxy, CORS, and headers**
- Forwarded headers are applied only when `ForwardedHeaders:KnownProxies` is configured (the middleware would otherwise trust any sender). Compose gives nginx a fixed address (`172.30.89.10` on subnet `172.30.89.0/24`) and configures it as the only trusted proxy; nginx now overwrites `X-Forwarded-For` with `$remote_addr`.
- CORS origins come from `Cors:AllowedOrigins` (Development: `localhost:5173`); wildcards are rejected and non-Development origins must be HTTPS. No CORS is configured by default.
- API responses: `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `Cache-Control: no-store`; HSTS outside Development. Swagger UI (Development) is exempt from the API CSP.
- Frontend (nginx): strict CSP (`default-src 'self'`, no inline script or third-party origins) and `Permissions-Policy`.

**Frontend**
- API key field; the key is held in JavaScript memory only (not in the bundle, storage, cookies, or URLs) and sent as `X-Api-Key`. Clear messages for `401`, `403`, and `429` (with the retry delay).
- Fixed a pre-existing bug: the HTTP client parsed only `application/json`, so `application/problem+json` error bodies were never read and the P1 concurrency-conflict handling could not trigger in the browser.

**Swagger**: API key security definition, so Swagger UI can send `X-Api-Key` via Authorize (Development only). Swagger UI itself did not render until the fix in `0ba69d6` (see [Pre-P5 security verification](#pre-p5-security-verification-2026-10-16)).

### P4 verification (2026-10-16)

| Check | Result |
|---|---|
| Release build with `CI=true` | Passed, 0 warnings |
| Application unit tests | 15 passed |
| API unit tests (incl. 20 new: key matching, hash format, startup validation) | 30 passed |
| Integration tests against SQL Server Testcontainers (incl. 24 new: 401/403, identical responses for wrong/missing keys, multiple and overlong keys, reader vs admin, security headers, no keys in logs, rate limits for reads/writes/anonymous/health, public vs restricted health) | 50 passed |
| Mutation check: key store accepts any key | 9 security tests failed as expected; code restored |
| `npm run build`; bundle contains no `localStorage`/`sessionStorage` | Passed |
| Key script hash vs. independent SHA-256 | Identical; 43-character key, 64-character lowercase hex hash |
| Compose: all services healthy with throwaway keys passed via shell environment (`.env` unchanged) | Passed after a one-time `docker compose down` (see below) |
| Live: anonymous `/health/live` and `/health/ready` return status only; `/health` and `/api` return `401` | Passed (direct and through nginx) |
| Live: reader GET `200`, POST `403`, `/health` `403`; admin detailed health, create `201`, stale update `409`, delete `204` | Passed |
| Live: security headers on frontend and API; Swagger security scheme | Passed |
| Live: anonymous limit `429` after 30 requests; spoofed `X-Forwarded-For` does not bypass it (direct or through nginx) | Passed |
| Live: from the nginx container, different forwarded client IPs get separate limits (forwarded headers trusted from nginx only) | Passed |
| API image as `Production`: refuses to start without keys or with a non-hash value (value not echoed); starts with a valid key, Swagger not served | Passed |

Findings during verification:
- Changing the Compose network to a fixed subnet left the existing `queryforge_default` network with broken DNS until it was recreated (`docker compose down`, then `up`). Fresh networks, including the same subnet and fixed IPs, worked. Documented in README troubleshooting.
- Direct requests from the host to `:5080` arrive from a different source address than the one nginx sees, so the two paths have separate per-IP buckets. Both are limited, and neither accepts spoofed forwarded headers.
- A change adding IPv4-mapped IPv6 proxy addresses was tested and found unnecessary (ASP.NET Core already handles mapped addresses), so it was removed.
- In Production the process exits with code 139 when startup validation fails (non-zero, but not a conventional error code).

**GitHub Actions:** P4 was committed as `7954daa` and pushed to `master`. [Run 38891263607](https://github.com/davidsky1992/QueryForge/actions/runs/38891263607) concluded **success** on `ubuntu-24.04` with no annotations:

| Job | Result | Duration |
|---|---|---|
| .NET build and tests | Success — all 12 steps, including the Release build (`CI=true`) and `Test`; `test-results` artifact uploaded (21.8 KB) | 56 s |
| Docker image builds | Success — all 8 steps (Compose validation; API, migrator, and web image builds; nothing is pushed or deployed) | 46 s |
| Frontend build | Success — all 8 steps | 16 s |

**Test counts:** 95 tests passed locally (15 Application, 30 API unit, 50 integration). On GitHub only the `Test` step's success is confirmed; the CI test count has not been independently confirmed (logs and artifacts require sign-in).

Browser-level behavior was verified afterwards; see [Pre-P5 security verification](#pre-p5-security-verification-2026-10-16).

### Pre-P5 security verification (2026-10-16)

Run against the local Compose stack with real locally generated keys: an admin and a reader key created with the key-generation logic of `scripts/New-QueryForgeApiKey.ps1`, with only their SHA-256 hashes in the git-ignored `.env`. No key, hash, or password was printed or committed. Existing SQL Server and Redis volumes were kept, and no `docker compose down` was needed (the network already used the fixed subnet).

| Check | Result |
|---|---|
| `docker compose config --quiet`; `docker compose up --build -d` | Passed; all services healthy, migrator exited 0 |
| `.env` hash lines (checked by name and format only) | Both present, 64-character lowercase hex |
| Anonymous: `/health/live` and `/health/ready` `200` (status only); `/health` and `/api/api-definitions` `401`; wrong key `401` | Passed (direct and through nginx) |
| `/health`: reader `403`, admin `200` | Passed |
| `/api/api-definitions`: reader and admin GET `200`; reader POST `403`; admin POST with an empty body `400` (validation, nothing created); reader GET through nginx `200` | Passed |
| Rate limits: anonymous 30 × `401` then `429`; `/health/ready` 60 × `200` then `429` (`/health/live` unaffected); reader key 120 × `200` then 5 × `429`, run twice | Passed |
| `429` response | `application/problem+json`, `Retry-After: 60` |
| Spoofed `X-Forwarded-For` sent directly to the API, or through nginx with a different value per request | Ignored; limits still applied |
| From the nginx container, different forwarded client IPs | Separate limits (forwarded headers trusted from nginx only) |
| Frontend and API security headers; no inline scripts in `index.html`; no `localStorage`/`sessionStorage`/`document.cookie`/IndexedDB in the source or the served bundle | Passed |

Browser checks (`http://127.0.0.1:8080` with DevTools, performed manually):

| # | Check | Result |
|---|---|---|
| 1 | Entering the reader key loads the list | Passed (database empty: "No API definitions found.") |
| 2 | Key gone after reload; nothing in Local/Session Storage or cookies | Passed |
| 3 | Key sent only in the `X-Api-Key` header, never in the URL | Passed |
| 4 | `429` message with retry delay ("Too many requests. Try again in 60 seconds.") after exhausting the reader quota | Passed |
| 5 | `401` message for a wrong key | Passed |
| 6 | `403` message for a reader write attempt | Passed |
| 7 | No Content-Security-Policy violations in the console | Passed |
| 8 | Concurrency-conflict message after a stale edit in a second tab | Passed |
| 9 | Swagger UI: `401` without a key, `200` after Authorize with the reader key, reader POST `403`, `401` after Logout | Passed after the fix below |

**Found and fixed: Swagger UI did not render** ("The provided definition does not specify a valid version field"), a defect present since the initial commit. The unused `Microsoft.AspNetCore.OpenApi` 8.0.31 package raised the transitive `Microsoft.OpenApi` to 1.6.30, which emits `"openapi": "3.0.4"`; the Swagger UI bundled with Swashbuckle 6.6.2 accepts only 3.0.0–3.0.3. Removing the package restored `Microsoft.OpenApi` 1.6.14 (`"openapi": "3.0.1"`). Release build with `CI=true` 0 warnings, 95 tests passed, no vulnerable packages, API image rebuilt. Committed as `0ba69d6` and pushed to `master`.

**CI test count confirmed** from the `test-results` artifact of the GitHub Actions run for `0ba69d6` (downloaded while signed in; counters read from the TRX `ResultSummary`, not inferred from the job status):

| TRX file | Total | Passed | Failed |
|---|---|---|---|
| `_runnervm8k3b6_2026-10-16_14_56_10.trx` (Application) | 15 | 15 | 0 |
| `_runnervm8k3b6_2026-10-16_14_56_11.trx` (API unit) | 30 | 30 | 0 |
| `_runnervm8k3b6_2026-10-16_14_56_40.trx` (integration) | 50 | 50 | 0 |
| **Total** | **95** | **95** | **0** |

The artifact belongs to the `0ba69d6` run: the TRX files were written at 14:56 UTC, within a minute of that commit (14:55:37 UTC); the previous commit `7954daa` was at 11:03 UTC. The run number was not recorded.

### P5 – Dynamic API Execution, milestone 1 (2026-10-26)

Administrators register GET operations under an API definition; executor and admin keys run them through `POST /api/execute/{definitionSlug}/{operationKey}` and receive a fixed envelope. **The endpoint is disabled** (`Execution:Enabled=false` in the shipped configuration); enabling it is a separate decision. Contract, configuration, SSRF design and limitations are documented in the README section [Dynamic API Execution](../README.md#dynamic-api-execution).

Decisions approved before implementation: fixed response envelope (no passthrough); upstream secrets in milestone 2 via configuration references, never in the database; no private-network allowlist; a distinct `executor` role with no access to management endpoints; milestone 1 executes only definitions with `AuthenticationType = None`.

Implemented in nine approved steps, each built with `CI=true` (0 warnings), fully tested and mutation-checked before commit:

| Step | Commit | Content | Tests after the step |
|---|---|---|---|
| 1 | `1159d9a` | `DestinationPolicy`: IPv4 deny-list, IPv6 `2000::/3` allow-list minus special ranges, IPv4-mapped checked as IPv4, port allow-list | 200 |
| 2 | `d2874a4` | `GuardedConnector`: resolve once, reject if any address is blocked, connect only to checked addresses; ports checked before DNS | 243 |
| 3 | `5ceca1e` | Hardened `SocketsHttpHandler` (ConnectCallback, no proxy, redirects, cookies, credentials or decompression) and HTTP/1.1 pinning; real-socket loopback tests | 270 |
| 4 | `b826b28` | Bounded response reading (UTF-8 JSON/text only, no content encoding), per-call timeout, error codes and 502/503/504 mapping | 327 |
| 5 | `f26da63` | `ApiOperation` and `AuditEvent` storage; template and parameter validation (linear-time anchored patterns); additive migration `AddApiOperationsAndAuditEvents` verified up, down and up | 454 |
| 6 | `7ee1bcb` | Operation management endpoints (reader reads, admin writes, rowVersion); `executor` role with no management access; audit of changes and concurrency rejections | 485 |
| 7 | `d32034d` | `ExecutionService`: status and auth checks, declared-parameters-only binding, percent-encoded URL building under the base URL, execution audit without values | 566 |
| 8 | `2c5ed46` | Endpoint behind `Execution:Enabled`, executor policy, `Execute` rate limit, concurrency cap (503), 16 KB body read without model binding, `RemoveAllLoggers` for the upstream client, startup validation (no `AllowHttp` in Production), Compose executor key slot | 593 |
| 9 | — | Security gate: milestone mutation re-run, README and status documentation (this section) | 593 |

Current local result: **593 tests passed** (230 Application, 270 API unit, 93 integration). The CI test count for the P5 commits has not been confirmed from a `test-results` artifact yet.

**Mutation re-run on the final code (Step 9).** Each guard was removed or weakened on purpose, only the relevant tests were run, and the file was restored byte-for-byte afterwards:

| Mutation | Result |
|---|---|
| Allow `169.254/16` (metadata) | Caught (4 tests) |
| Allow IPv6 outside `2000::/3` | Caught (17) |
| Resolve DNS again at connect time (rebinding) | Caught (5) |
| Filter mixed DNS answers instead of rejecting | Caught (5) |
| Remove `ConnectCallback` (unchecked sockets) | Caught (25) |
| Follow redirects | Caught (4) |
| Do not pin HTTP/1.1 (HTTP/3 bypass) | Caught (3) |
| Read upstream response without a size limit | Caught (1) |
| Read the request body without a size limit | Caught (2) |
| Ignore unknown parameters | Caught (3) |
| Do not percent-encode path values | Caught (7) |
| Allow `.`/`..` segments in templates | Caught (4) |
| Operation update does not bump `RowVersion` | Caught (1, integration) |
| Give `executor` read access to management endpoints | Caught (1, integration) |
| Keep `IHttpClientFactory` loggers (URLs logged) | Caught (2, integration) |
| Expose the execute endpoint while disabled | Caught (1, integration) |
| Build the upstream client without `GuardedConnector` | Caught (16, integration) |

Findings during P5 that strengthened tests or code:
- **Weak tests found by mutations and fixed:** a content-encoding test that also failed for invalid JSON; a slow-body timeout test that would hang instead of failing; a connector test that did not cover non-socket exceptions; a disabled-endpoint test that could not tell the routing 404 from the service's 404; a body-limit test that never reached the counting loop (moved into the unit-tested `BoundedBodyReader`).
- **Defects found by tests and fixed:** decimal parameter bounds were reported as `1` when created but `1.0000000000` when read back (database scale); MVC Trace logging of bound action arguments would have logged caller parameters, so the execute action reads its body itself.
- **Mutation that survives by design:** removing the final "URL is still under the base URL" check. Percent-encoding makes the check unreachable, so it stays as defence in depth and is documented as untested on its own.

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
- **Confirmed on GitHub:** [run 38884867143](https://github.com/davidsky1992/QueryForge/actions/runs/38884867143) for `34a2cca` concluded **success** on `ubuntu-24.04` with the upgraded actions:

  | Job | Result | Duration |
  |---|---|---|
  | .NET build and tests | Success — all steps succeeded, `test-results` artifact uploaded (13 KB) | 65 s |
  | Docker image builds | Success — Compose validation and API, migrator, and web image builds (images are built only; nothing is pushed or deployed) | 58 s |
  | Frontend build | Success — `npm ci`, type-check and Vite build | 16 s |

  The run reported no annotations; the earlier Node.js 20 deprecation and Ubuntu 26 migration notices no longer appear.
- **Not confirmed:** NuGet cache reuse (the restore and save steps succeeded, but cache hits were not checked in the logs) and the CI test count (logs and artifacts require sign-in). 51 tests remain verified locally only.

## Known limitations
- Authentication uses shared API keys: no per-user identities, single sign-on, key expiry, or runtime key management; rotation requires a configuration change and restart.
- An API key entered in the browser can be read by any script running in that page; the strict CSP reduces but does not remove this risk.
- Rate limits are in memory per API instance (not shared across instances), and per-IP limits depend on `ForwardedHeaders:KnownProxies` matching the real proxy in each environment.
- Unknown routes return `401` to anonymous callers (fallback policy) instead of `404`.
- Actions are pinned to major-version tags, not commit SHAs.
- The project still builds the frontend with Node.js 20 (`node-version: 20` in CI, `node:20-alpine` in the frontend Dockerfile), which reached end of life in April 2026. This is the project toolchain, separate from the Node.js 24 runtime used by the actions themselves.
- Base images use floating tags (`mssql/server:2022-latest`, `redis:7-alpine`, `dotnet/*:8.0`, `node:20-alpine`, `nginx-unprivileged:1.27-alpine`); pin digests for reproducible release builds.
- The API image uses the Debian-based ASP.NET runtime (with `curl` for health checks) rather than a smaller chiseled image.
- Local Compose uses the `sa` login, an unencrypted connection, and password-less Redis; acceptable on loopback only.
- Integration tests need Docker and about 2 GB of RAM for the SQL Server container.
- Redis is not covered by integration tests and is not yet used for business caching.
- While Redis is down, cache operations wait for StackExchange.Redis timeouts (~5 s); acceptable while nothing uses the cache.
- Search does not escape `%`/`_` wildcards (parameterized, so not an injection risk).
- Frontend is a single `App.tsx` component without routing or tests.
- `QueryForge.Api.http` still targets the template `/weatherforecast` endpoint.
- Operation execution (P5 milestone 1): disabled by default; upstream authentication `None` only; limits per instance; internal services reachable through public addresses are not detected and no network egress rules are configured; static deny-list (NAT64 blocked); DNS rebinding proven with a fake resolver; the final base-URL containment check has no test of its own; TLS not tested separately; GET, path and query parameters only. Details in the README [limitations](../README.md#limitations).
- `AuditEvents` has no retention policy and no read endpoint.

## Next steps

### 1. Before enabling execution (separate approval)
P5 milestone 1 is complete with `Execution:Enabled=false`. Turning it on should follow:
- Confirming the CI test count (593 expected) for the latest commit from the `test-results` artifact, including the real-socket tests on Ubuntu.
- An explicit decision on where it is enabled (local Compose only, or a deployed environment) and, for any deployment, network egress rules as a second layer.
- Generating an executor key (`scripts/New-QueryForgeApiKey.ps1 -Role executor`) and a manual end-to-end check against a real public test API.

### 2. P5 milestone 2 (proposed)
- Upstream API-key authentication through configuration secret references (never stored in the database), with redaction tests.
- Admin UI for operations and a "try it" panel for executor keys.
- An admin-only audit read endpoint and a retention policy.

### 3. Later
- Azure deployment: push the API image to Azure Container Registry, provision App Service, Azure SQL, Azure Cache for Redis, and Key Vault (including API key hashes); configure `ForwardedHeaders:KnownProxies` for the App Service front end; run the migration bundle in the pipeline.
- Identity-based authentication (Microsoft Entra ID / OIDC with JWT bearer tokens) if per-user access or SSO is needed; the role-based policies can stay.

CI follow-ups (small, separate changes):
- Upgrade the project's Node.js toolchain from 20 to a supported LTS (CI, frontend Dockerfile, local setup).
- Publish test counts to the job summary (`$GITHUB_STEP_SUMMARY`) so they are visible on the run page.
- Evaluate Ubuntu 26 runners before moving off `ubuntu-24.04`.
- Optionally pin actions to commit SHAs (with Dependabot updates).
