# QueryForge

QueryForge is a portfolio-oriented API management platform built as a modular monolith.

## Stack

- ASP.NET Core Web API on .NET 8
- Entity Framework Core with SQL Server
- Redis distributed cache wiring
- React + TypeScript + Vite
- xUnit tests
- Docker and Docker Compose for the full local stack (API, frontend, SQL Server, Redis)

## Prerequisites

| Tool | Needed for |
|---|---|
| Docker Desktop (Compose v2) | Full-stack containers, SQL Server/Redis, integration tests |
| .NET SDK 8.0.4xx (see `global.json`) | Running the API and tests on the host |
| Node.js 20 + npm | Running the frontend on the host |
| `dotnet-ef` 8.0.x (`dotnet tool install --global dotnet-ef`) | Applying migrations from the host (Option B only) |

## Local Configuration

Credentials are never stored in tracked files. Docker Compose reads the SQL Server password from a git-ignored `.env` file; when the API runs on the host it reads its connection string from .NET user secrets (stored in your user profile, outside the repository).

Create your local `.env` (skip if it already exists — never overwrite it) and replace the placeholder with a strong password:

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
notepad .env
```

> The SA password is applied only when the SQL Server volume is first created. If you change it later, either keep using the original password or recreate the volume with `docker compose down -v` (this deletes local data).

## Option A: Full Stack with Docker Compose

Builds and starts SQL Server, Redis, the migrator, the API, and the frontend:

```powershell
docker compose up --build -d
docker compose ps
```

Startup order is enforced with health checks: `sqlserver` (healthy) → `migrator` (completed successfully) → `api` (healthy) → `web`. The first build takes several minutes; later builds reuse cached layers.

| URL | Service |
|---|---|
| http://localhost:8080 | Frontend (nginx), also proxies `/api` and `/health*` to the API. Enter an API key in the header field to load data. |
| http://localhost:5080/swagger | API Swagger UI (direct access, Development only); use **Authorize** to send an API key |
| http://localhost:8080/health/ready | Public readiness status through the frontend proxy (detailed `/health` requires an admin key) |
| `127.0.0.1,1433` | SQL Server for SSMS/Azure Data Studio (`sa` + your `.env` password) |
| `127.0.0.1:6379` | Redis |

All host ports are bound to `127.0.0.1` only and can be changed in `.env` (`WEB_HOST_PORT`, `API_HOST_PORT`, `SQLSERVER_HOST_PORT`, `REDIS_HOST_PORT`). SQL Server and Redis ports are exposed only so Option B and local database tools work; containers talk to each other through the Compose service names `sqlserver`, `redis`, and `api`.

### How the frontend reaches the API

The browser only ever talks to `http://localhost:8080`. The frontend image is built with an empty `VITE_API_BASE_URL`, so the app calls relative URLs such as `/api/api-definitions`; nginx inside the `web` container forwards `/api/*` and `/health*` to `http://api:8080` on the Compose network. Because everything is same-origin, no CORS configuration is needed, and no hostnames or secrets are compiled into the JavaScript bundle (`VITE_*` values are public by design). The API key is typed into the page at runtime and kept in memory only (see [Authentication and Authorization](#authentication-and-authorization)).

nginx has a fixed address (`172.30.89.10`) on the Compose network, and the API trusts `X-Forwarded-For`/`X-Forwarded-Proto` from that address only, so per-client rate limits use the real client IP. nginx overwrites any client-supplied `X-Forwarded-For`.

### Database migrations

The `migrator` service runs an [EF Core migration bundle](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying#bundles) built from the same source as the API. It applies pending migrations (creating the database on first run) and exits; already-applied migrations are skipped, so re-running is safe. The API never changes the schema at startup. To apply migrations manually:

```powershell
docker compose run --rm migrator
```

### Stop, restart, and clean up

```powershell
docker compose stop            # stop containers, keep everything
docker compose start           # start them again
docker compose down            # remove containers and network; data volumes are kept
docker compose up -d           # recreate; existing data is still there
docker compose down -v         # also delete SQL Server and Redis data (irreversible)
docker image rm queryforge-api:local queryforge-migrator:local queryforge-web:local   # remove built images
```

### Troubleshooting

| Symptom | Fix |
|---|---|
| `required variable MSSQL_SA_PASSWORD is missing a value` | Create `.env` from `.env.example`. |
| `migrator` exits non-zero / `Login failed for user 'sa'` | The volume was created with a different password. Use the original password or `docker compose down -v`. |
| `bind: address already in use` / port is already allocated | Another process uses the port (for example the host-run API or another SQL Server). Change the `*_HOST_PORT` value in `.env`. |
| SQL Server keeps restarting or is very slow | Docker Desktop needs at least ~3 GB RAM for this stack; SQL Server is capped at 2 GB. Stop other containers. |
| Frontend shows `Request failed (502)` | The API is not healthy yet: check `docker compose ps` and `docker compose logs api`. |
| Windows-run API or SSMS times out connecting to `localhost,1433` | Use `127.0.0.1,1433`. Ports are bound to `127.0.0.1` only, and SqlClient connections by the name `localhost` time out through Docker Desktop's loopback port forwarding. |
| Password contains `$` or `;` | Wrap `$` values in single quotes in `.env`; do not use `;` (it breaks the connection string). |
| Every API call returns `401` in the frontend or Swagger | No API key configured or entered. Generate one (see [Authentication and Authorization](#authentication-and-authorization)), put its hash in `.env`, run `docker compose up -d`, and enter the key. |
| After updating to the P4 Compose file, `migrator` fails with `error: 35` / cannot reach `sqlserver` | The Compose network now has a fixed subnet. Recreate it once: `docker compose down` then `docker compose up -d` (data volumes are kept). |
| `Pool overlaps with other one on this address space` | Another Docker network uses `172.30.89.0/24`. Change the subnet and both `172.30.89.10` addresses in `docker-compose.yml`. |
| Checking the Compose file | Use `docker compose config --quiet`. Without `--quiet`, the rendered output contains the resolved connection strings including the password — do not paste it into issues or logs. |

### Configuration per environment

| Setting | Host-run (Option B, `Development`) | Docker Compose | Production (Azure) |
|---|---|---|---|
| `ConnectionStrings:SqlServer` | User secrets | `ConnectionStrings__SqlServer` from Compose (`sqlserver,1433`) | App setting / Key Vault reference |
| `ConnectionStrings:Redis` | `appsettings.Development.json` (`127.0.0.1:6379`) | `ConnectionStrings__Redis` from Compose (`redis:6379`) | App setting; if unset, the in-memory cache is used |

The base `appsettings.json` contains no hosts or credentials, and `appsettings.Development.json` is excluded from the Docker image.

## Option B: API and Frontend on Windows, Infrastructure in Docker

Faster inner loop with `dotnet watch` and Vite hot reload. Start infrastructure only:

```powershell
docker compose up -d sqlserver redis
```

Store the API connection string as a user secret, using the same password as `.env`:

```powershell
dotnet user-secrets set "ConnectionStrings:SqlServer" "Server=127.0.0.1,1433;Database=QueryForge;User Id=sa;Password=<your-password>;TrustServerCertificate=True;Encrypt=False" --project src/QueryForge.Api
```

Outside Development, supply it through the `ConnectionStrings__SqlServer` environment variable or a secret store instead.

Add an API key hash to user secrets (the script prints the exact commands):

```powershell
powershell -File scripts/New-QueryForgeApiKey.ps1 -Name local-admin -Role admin
dotnet user-secrets set "Security:ApiKeys:0:Name" "local-admin" --project src/QueryForge.Api
dotnet user-secrets set "Security:ApiKeys:0:Hash" "<hash printed by the script>" --project src/QueryForge.Api
dotnet user-secrets set "Security:ApiKeys:0:Roles" "admin" --project src/QueryForge.Api
```

Apply database migrations:

```powershell
dotnet ef database update --project src/QueryForge.Infrastructure --startup-project src/QueryForge.Api
```

Run the API:

```powershell
dotnet watch --project src/QueryForge.Api
```

Run the frontend:

```powershell
npm install --prefix src/QueryForge.Web
npm run dev --prefix src/QueryForge.Web
```

Open Swagger at http://localhost:5111/swagger and the Vite app at http://localhost:5173. Without `VITE_API_BASE_URL`, the dev server calls the API at `http://localhost:5111`; the API allows that origin through its Development-only CORS policy. To point the Vite dev server at the containerized API instead, set `VITE_API_BASE_URL=http://localhost:5080` before `npm run dev`.

## Local Docker Compose vs Azure App Service

Docker Compose here is a local development environment, not a deployment target. The API image is the deployable artifact; the rest maps to managed Azure services:

| Concern | Local Docker Compose | Azure App Service (planned) |
|---|---|---|
| API | `api` container from `src/QueryForge.Api/Dockerfile` | Same image from Azure Container Registry on App Service for Containers (Linux), `WEBSITES_PORT=8080` |
| Configuration | `.env` + Compose environment variables | App Service application settings; secrets as Key Vault references |
| Environment | `Development` (Swagger, detailed errors) | `Production` |
| Database | SQL Server container, `sa` login, `TrustServerCertificate=True` | Azure SQL Database with encrypted connections and a least-privilege login or managed identity |
| Cache | Redis container, no password | Azure Cache for Redis with TLS and access keys or Entra ID |
| Migrations | `migrator` container before the API starts | Same migration bundle as a deployment pipeline step, before swapping slots |
| TLS / HTTPS | Plain HTTP on loopback | Terminated by App Service; HTTPS-only setting enabled |
| Health | Compose health checks (`/health/live`) | App Service Health check pointed at `/health/ready` (anonymous, status only) |
| API keys | Hashes from `.env` (optional; without them protected calls return 401) | Hashes as Key Vault references; startup fails if none are configured |
| Forwarded headers | Trusted from the nginx container's fixed IP only | Configure `ForwardedHeaders:KnownProxies` for the actual front end before relying on per-IP limits |
| Frontend | nginx container proxying `/api` | Azure Static Web Apps (linked backend) or a second App Service; either same-origin proxying or `VITE_API_BASE_URL` plus a CORS policy |
| Scaling | Single instance | Multiple instances; schema changes stay out of app startup |

## Authentication and Authorization

Every endpoint requires an API key unless it is explicitly public (fail closed). Clients send the key in the `X-Api-Key` header.

| Endpoint | Access |
|---|---|
| `GET /api/api-definitions`, `GET /api/api-definitions/{id}` | `reader` or `admin` key |
| `POST`, `PUT`, `DELETE` on `/api/api-definitions` | `admin` key |
| `GET /api/api-definitions/{id}/operations[/{operationId}]` | `reader` or `admin` key |
| `POST`, `PUT`, `DELETE` on `/api/api-definitions/{id}/operations` | `admin` key |
| `POST /api/execute/{definitionSlug}/{operationKey}` | `executor` or `admin` key; **only exists when `Execution:Enabled` is true** (off by default) |
| `GET /health` (detailed) | `admin` key |
| `GET /health/live`, `GET /health/ready` | Public |
| `/swagger` | Public, Development only |
| Anything else (including unknown paths) | Requires a key; anonymous callers get `401` rather than `404` |

Roles: `reader` reads definitions and operations; `admin` also changes them, executes operations and sees detailed health; `executor` can only execute operations and gets `403` on every management endpoint.

Missing or invalid keys get `401` with `WWW-Authenticate: ApiKey header="X-Api-Key"`; a valid key without the required role gets `403`. Both are ProblemDetails responses that never echo the key, and a wrong key gets exactly the same response as a missing one.

### Creating and configuring keys

```powershell
powershell -File scripts/New-QueryForgeApiKey.ps1 -Name local-admin -Role admin
```

The script generates 32 random bytes (a 43-character key), shows the key once, and prints its SHA-256 hash with configuration examples. **Only the hash is configured**; the key itself is given to the client and never stored by QueryForge.

| Where | How |
|---|---|
| Docker Compose | `QUERYFORGE_ADMIN_KEY_SHA256`, `QUERYFORGE_READER_KEY_SHA256` and/or `QUERYFORGE_EXECUTOR_KEY_SHA256` in `.env` (each optional), then `docker compose up -d` |
| API on the host (Option B) | User secrets `Security:ApiKeys:<n>:Name`, `:Hash`, `:Roles` |
| Production | `Security__ApiKeys__<n>__Name/Hash/Roles` app settings, with the hash as a Key Vault reference |

Startup validation rejects malformed hashes, unknown roles, and duplicate names or hashes. Outside `Development`, the API refuses to start without at least one key. In `Development`, keys are optional: the API starts and protected endpoints return `401` until a key is configured.

**Rotation:** add the new key's hash in a new slot, switch clients to the new key, then remove the old hash. Both keys work during the overlap. Configuration is read at startup, so changes need a restart (App Service restarts the app when settings change).

**How keys are checked:** the presented key is hashed with SHA-256 and compared with every configured hash using `CryptographicOperations.FixedTimeEquals`, without stopping at the first match. Plain SHA-256 is appropriate because keys are high-entropy random values, not passwords. Keys longer than 256 characters are rejected before hashing. Only the key's *name* is ever used in logs and rate-limit partitions.

### Frontend

The frontend has an API key field. The key is kept in JavaScript memory only — never in the bundle, `localStorage`, `sessionStorage`, cookies, or URLs — so it must be re-entered after a page reload. nginx sends a strict Content-Security-Policy (`default-src 'self'`, no inline scripts or third-party origins) to limit what injected script could do. A key held by a browser is still only as safe as that page; use a `reader` key where write access is not needed.

## Rate Limiting

ASP.NET Core rate limiting with fixed one-minute windows. Exceeding a limit returns `429` ProblemDetails with a `Retry-After` header.

| Policy | Default | Partition | Applies to |
|---|---|---|---|
| `Reads` | 120 / min | API key name | `GET` API definitions |
| `Writes` | 30 / min | API key name | `POST`, `PUT`, `DELETE` |
| `Anonymous` | 30 / min | Client IP | Unauthenticated requests to protected endpoints (counted before the `401`, slowing down key guessing) |
| `Health` | 60 / min | Client IP | `/health/ready` and `/health` |
| `Execute` | 60 / min | API key name | `POST /api/execute/...` (each request makes an outbound call) |

`/health/live` is not limited because container health checks call it every 10 seconds. Tune limits with `RateLimiting:<Policy>:PermitLimit` and `RateLimiting:<Policy>:WindowSeconds` (for example `RateLimiting__Writes__PermitLimit=60` as an environment variable); invalid values stop startup.

Counters are kept **in memory per API instance**. With several instances each one counts separately, so the effective limit is multiplied by the instance count; a shared limit would need a distributed store (for example Redis) or a gateway such as Azure API Management. Per-IP partitions rely on `ForwardedHeaders:KnownProxies` listing the real reverse proxy; when it is empty, forwarded headers are ignored and the connection address is used.

## Health Checks

| Endpoint | Access | Response | Status codes |
|---|---|---|---|
| `GET /health/live` | Public, not rate limited | `{"status":"Healthy"}`; no dependencies are checked | `200` |
| `GET /health/ready` | Public, rate limited | `{"status":"..."}` only; runs all checks but reveals no dependency names | `200` Healthy/Degraded, `503` Unhealthy |
| `GET /health` | `admin` key | Status, generic description, and duration per dependency (SQL Server, Redis) | `200` Healthy/Degraded, `503` Unhealthy, `401`/`403` |

SQL Server is required, so its failure makes the API `Unhealthy`. Redis only backs the optional distributed cache, so its failure reports `Degraded`. Each check has a 3 s timeout. Exception messages and connection details are never returned.

## Optimistic Concurrency

Every API definition response includes a `rowVersion`. Clients must send it back when changing a record:

- `PUT /api/api-definitions/{id}` with `rowVersion` in the JSON body.
- `DELETE /api/api-definitions/{id}?rowVersion={rowVersion}` (URL-encoded).

If the record changed since it was read, the API returns `409 Conflict` with `"code": "concurrency_conflict"`. A missing or malformed `rowVersion` returns `400`. Operations (below) work the same way.

## Dynamic API Execution

QueryForge can call a registered upstream HTTP API on a caller's behalf. Administrators register **operations**; callers supply **parameter values only**. The URL, method, headers and upstream authentication always come from the registration, never from the caller.

> **Status:** milestone 1 is implemented and tested, but the endpoint is **disabled** (`Execution:Enabled=false`). Enabling it is a separate, explicit decision.

### Registering an operation (admin)

An operation belongs to an API definition. The definition supplies the host (`baseUrl`) and its `authenticationType`; the operation adds a relative path template and the parameters a caller may send.

```http
POST /api/api-definitions/{definitionId}/operations
X-Api-Key: <admin key>

{
  "key": "current-weather",
  "method": 0,                               // GET (the only method in milestone 1)
  "pathTemplate": "/v1/forecast/{city}",
  "parameters": [
    { "name": "city", "location": 0, "type": 0, "required": true,  "maxLength": 64 },
    { "name": "days", "location": 1, "type": 1, "required": false, "minimum": 1, "maximum": 7, "default": "3" }
  ],
  "timeoutSeconds": 10,                      // 1–30, default 10
  "maxResponseBytes": 262144,                // up to 1 MiB, default 256 KiB
  "enabled": true
}
```

Enums are numbers: `location` 0 = path, 1 = query; `type` 0 = string, 1 = integer, 2 = number, 3 = boolean.

Registration rules:

- `pathTemplate` starts with `/` and contains only unreserved literal segments (`A-Z a-z 0-9 - . _ ~`) and whole-segment `{placeholders}`. No scheme, host, `?`, `#`, `%`, `\`, `//`, `.` or `..`.
- Every placeholder has exactly one matching `path` parameter (case-sensitive), and every `path` parameter is required.
- Up to 20 parameters. `maxLength` (≤ 1024) applies to strings, `minimum`/`maximum` to numbers. `allowedValues` and `default` must satisfy the declared rules.
- `pattern` must match the whole value. It is anchored automatically and evaluated with .NET's linear-time `NonBacktracking` engine and a 100 ms timeout. Backreferences and lookarounds are rejected.
- Updates and deletes need `rowVersion`; changes are audited.

### Executing (executor or admin)

```http
POST /api/execute/weather/current-weather
X-Api-Key: <executor key>
Content-Type: application/json

{ "parameters": { "city": "Hanoi", "days": 2 } }
```

If the upstream answered, with any status, the response is `200` with a fixed envelope. Upstream headers are never passed through.

```json
{ "upstreamStatus": 200, "contentType": "application/json", "body": { "temp": 31 }, "durationMs": 143 }
```

`body` is the parsed JSON, a JSON string for `text/*`, or `null` for an empty body. A redirect is reported as its status (for example `302`) and is never followed.

| Status | `code` | Meaning |
|---|---|---|
| `400` | `validation_error` | Unknown, missing or invalid parameters (including `""`, `.` or `..` as a path value); malformed JSON |
| `401` / `403` | — | Missing or invalid key / key without `executor` or `admin` |
| `404` | `not_found` | Unknown definition slug or operation key |
| `409` | `operation_disabled` | Operation disabled, or definition not `Active` |
| `409` | `upstream_auth_not_supported` | Definition's `authenticationType` is not `None` (milestone 1) |
| `413` | `request_too_large` | Request body over 16 KB |
| `415` | — | Request body is not `application/json` |
| `429` | — | `Execute` rate limit |
| `502` | `upstream_destination_blocked` | Target not allowed: blocked address, port or scheme, or a base URL with userinfo, query or fragment |
| `502` | `upstream_unreachable` | DNS failure, connection refused, TLS failure |
| `502` | `upstream_response_too_large` / `upstream_unsupported_content_type` / `upstream_invalid_response` | Over `maxResponseBytes`; not UTF-8 JSON or text; malformed, compressed or truncated response |
| `503` | `execution_capacity_exceeded` | Too many executions in flight (with `Retry-After`) |
| `504` | `upstream_timeout` | No complete response within `timeoutSeconds` |
| `499` | — | The caller disconnected; the upstream call is cancelled |

### Configuration

| Setting | Default | Notes |
|---|---|---|
| `Execution:Enabled` | `false` | When false, the execute route does not exist |
| `Execution:AllowHttp` | `false` | Plain-HTTP upstreams; **startup fails if enabled in Production** |
| `Execution:AllowedPorts` | `80`, `443` | A configured list replaces the defaults |
| `Execution:MaxConcurrentExecutions` | `20` | Per API instance; requests over the cap get `503` immediately (no queue) |
| `RateLimiting:Execute` | 60 / min per key | See [Rate Limiting](#rate-limiting) |

### SSRF protection

The caller never supplies a URL. The upstream URL is the definition's `baseUrl` plus the registered template, with each value percent-encoded into a single path segment or query pair. The destination is then checked **when the connection is made**, not by inspecting strings:

1. **Resolve once, connect to what was checked.** A `SocketsHttpHandler.ConnectCallback` resolves the host once, rejects the connection if **any** resolved address is blocked, and opens the socket to one of the checked addresses. A DNS answer cannot change between the check and the connect (DNS rebinding), and a mixed public/private answer is rejected outright. TLS still validates the certificate against the host name.
2. **Blocked IPv4:** `0/8`, `10/8`, `100.64/10`, `127/8`, `169.254/16` (cloud metadata), `172.16/12` (includes the Compose network), `192.0.0/24`, `192.0.2/24`, `192.88.99/24`, `192.168/16`, `198.18/15`, `198.51.100/24`, `203.0.113/24`, `224/4`, `240/4`.
3. **IPv6:** only global unicast `2000::/3` is allowed, minus `2001::/23` (including Teredo), `2001:db8::/32`, `2002::/16` (6to4) and `3fff::/20`. So `::1`, `fc00::/7` (including AWS's `fd00:ec2::254`), `fe80::/10`, multicast, NAT64 and IPv4-compatible forms are blocked. IPv4-mapped addresses (`::ffff:a.b.c.d`) are checked as IPv4.
4. **Ports and scheme:** only the allowed ports (checked before any DNS lookup) and HTTPS, unless `AllowHttp`.
5. **Nothing else can redirect the connection:** proxies are disabled, redirects are not followed, requests are pinned to HTTP/1.1 (HTTP/3 uses QUIC and would bypass `ConnectCallback`), and IP literals in any form (`127.1`, `2130706433`, `0x7f000001`, `[::ffff:127.0.0.1]`) are parsed and checked like resolved addresses.
6. **No ambient credentials or state:** upstream requests carry only `User-Agent: QueryForge/1.0` and `Accept`. No caller header (including `X-Api-Key`, `Authorization`, `Cookie`, `X-Forwarded-*`) is copied, and cookies, Windows credentials and automatic decompression are disabled.
7. **Bounded resources:** request bodies up to 16 KB, at most 20 parameters of at most 1 KB each, response headers up to 32 KB, response bodies read only up to `maxResponseBytes` (declared oversized bodies are rejected unread), a per-operation timeout covering headers and body, a connect timeout, a concurrency cap and a per-key rate limit. A client disconnect cancels the upstream call.

How each part is verified (all tests run without internet access):

| Requirement | Evidence |
|---|---|
| Address rules, range boundaries, IPv6 and IPv4-mapped forms | 105 table-driven tests in `DestinationPolicyTests` |
| DNS rebinding and mixed answers | `GuardedConnectorTests`: a fake resolver answers public then loopback; the socket goes only to the checked public address, and a mixed answer opens no connection |
| Blocking with real DNS and sockets | `UpstreamHandlerTests` and `ExecutionEndpointTests`: `127.0.0.1`, `127.1`, `2130706433`, `0x7f.0.0.1`, `[::ffff:127.0.0.1]`, `[::1]` and `localhost` produce **zero TCP connections** to a listening loopback server, with that server's port and plain HTTP deliberately allowed so only the address rules can block |
| Redirects, cookies, decompression, HTTP/3, headers | Real-socket tests against loopback servers |
| End-to-end pipeline | `ExecutionEndpointTests` resolve the shipped DI graph (real resolver, guarded handler, no HTTP loggers) and run the whole request through it |
| Tests can detect regressions | Each guard was removed or weakened on purpose (mutation testing) and the named tests failed; see `docs/PROJECT_STATUS.md` |

### Audit trail

Configuration changes (`operation.created`, `.updated`, `.deleted`, including concurrency rejections) and every execution attempt on an existing operation (`operation.executed`) are written to the `AuditEvents` table. Each row holds the time, the API key **name**, the action, the operation id, the outcome (`Succeeded`, `Failed` or `Rejected`), upstream status, duration, error code and trace id. **No parameter values, bodies, headers, keys or credentials are stored.** Audit rows are written through their own `DbContext`, and are written even if the caller disconnects. If an audit write fails, the failure is logged with identifiers and the request still reports the real outcome. There is no retention policy or read endpoint yet.

Parameter values are also kept out of logs: `IHttpClientFactory`'s default loggers, which log full URLs, are removed for the upstream client, and the execute action reads its body itself because MVC logs bound action arguments at Trace level.

### Limitations

- **Upstream authentication:** only `None` in milestone 1. API-key upstreams (via configuration secret references, never stored in the database) are planned for milestone 2.
- **Limits are per instance:** rate limits and the concurrency cap are kept per API instance and are not shared.
- **Internal services on public addresses** (hairpin NAT, your own public endpoint, a public host that proxies onward) are not detected. Administrators are trusted to register only intended public hosts; network-level egress rules remain the recommended second layer, and Compose does not configure any.
- **Static deny-list:** new special-purpose ranges need a code change. NAT64 (`64:ff9b::/96`) is blocked, so an IPv6-only deployment relying on DNS64 cannot reach IPv4-only upstreams.
- **DNS rebinding** is proven with a fake resolver, not a live rebinding DNS server. On Linux a slow `getaddrinfo` call is abandoned at the timeout rather than aborted.
- **Defence-in-depth check not tested on its own:** the final check that the built URL is still under `baseUrl` cannot be reached while values are percent-encoded, so no test can trigger it (its mutation survives).
- **TLS:** tests use plain HTTP on loopback; certificate validation uses the .NET defaults and is only verified as not overridden.
- **Scope:** GET only, path and query parameters only, no request bodies, no retries, no UI.

## Running Tests

| Project | Type | Needs Docker |
|---|---|---|
| `QueryForge.Application.Tests` | Unit tests for application services, validation, parameter binding and URL building | No |
| `QueryForge.Api.Tests` | Unit tests for middleware, security and health output, plus execution networking tests with real sockets on loopback | No (needs loopback networking only) |
| `QueryForge.Api.IntegrationTests` | HTTP tests through the real ASP.NET Core pipeline against a disposable SQL Server container (Testcontainers) | **Yes** |

Run everything (Docker Desktop must be running):

```powershell
dotnet test QueryForge.sln
```

Run only the tests that do not need Docker:

```powershell
dotnet test QueryForge.sln --filter "FullyQualifiedName!~IntegrationTests"
```

Integration test notes:

- One SQL Server container is started per test run, migrated with the real EF Core migrations, and removed afterwards. Each test clears the data before it runs, so tests are independent of order.
- The test host runs in the `Testing` environment: developer user secrets, Swagger, and local CORS are not loaded, so tests never touch your development database. Redis is disabled (in-memory cache).
- Without Docker the integration tests fail immediately rather than being skipped, so CI cannot pass silently without them.
- The container uses the same `mssql/server:2022-latest` image as Compose and needs about 2 GB of RAM. On an 8 GB machine, stop the Compose SQL Server (`docker compose stop sqlserver`) while running integration tests if memory is tight.

## Continuous Integration

GitHub Actions ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs on pushes and pull requests to `master`:

- **.NET job:** `dotnet restore`, `dotnet build -c Release`, and `dotnet test` for all three test projects (Docker is available on `ubuntu-latest`). TRX test results are uploaded as an artifact even when tests fail.
- **Frontend job:** `npm ci` and `npm run build` (TypeScript type-check and Vite build).
- **Docker job:** validates `docker-compose.yml` and builds the API, migrator, and web images (nothing is pushed).

Any failing step fails the workflow. The workflow has read-only repository permissions and uses no secrets. In CI (`CI=true`), [Directory.Build.props](Directory.Build.props) treats all warnings — including NuGet vulnerability warnings — as errors. To reproduce locally:

```powershell
$env:CI = "true"; dotnet build QueryForge.sln -c Release; Remove-Item Env:CI
```

[global.json](global.json) pins the .NET 8.0.4xx SDK feature band (rolling forward to newer feature bands).

## Windows 8 GB RAM Notes

- For day-to-day coding prefer Option B (`dotnet watch` and Vite HMR); use Option A to check the containerized build.
- Memory limits: SQL Server `2g`, API `512m`, migrator `256m`, Redis `256m`, frontend `64m`.
- Integration tests start their own SQL Server container (~2 GB); run `docker compose stop` first if memory is tight.

## Security Notes

- Never commit `.env` or real connection strings; `.env.example` contains placeholders only.
- The `sa` login and `TrustServerCertificate=True` are acceptable for local development only.
- Do not store third-party API credentials in API definition records.
- Operation execution is disabled by default. Before enabling it, read [Dynamic API Execution](#dynamic-api-execution), especially its limitations.
- Do not execute user-provided SQL or dynamically generated database commands.
- Swagger is enabled in Development only. CORS origins come from `Cors:AllowedOrigins` (only `localhost:5173` in `appsettings.Development.json`); wildcards are rejected and non-Development origins must be HTTPS.
- API responses carry `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: default-src 'none'`, and `Cache-Control: no-store`; HSTS is enabled outside Development.
- API keys are shared secrets: there are no per-user identities, expiry, or self-service rotation. Treat each key like a password and prefer narrowly scoped `reader` keys.
- Integration tests generate random keys at runtime; no real or test credentials are committed.
