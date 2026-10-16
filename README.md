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
| `GET /health` (detailed) | `admin` key |
| `GET /health/live`, `GET /health/ready` | Public |
| `/swagger` | Public, Development only |
| Anything else (including unknown paths) | Requires a key; anonymous callers get `401` rather than `404` |

Missing or invalid keys get `401` with `WWW-Authenticate: ApiKey header="X-Api-Key"`; a valid key without the required role gets `403`. Both are ProblemDetails responses that never echo the key, and a wrong key gets exactly the same response as a missing one.

### Creating and configuring keys

```powershell
powershell -File scripts/New-QueryForgeApiKey.ps1 -Name local-admin -Role admin
```

The script generates 32 random bytes (a 43-character key), shows the key once, and prints its SHA-256 hash with configuration examples. **Only the hash is configured**; the key itself is given to the client and never stored by QueryForge.

| Where | How |
|---|---|
| Docker Compose | `QUERYFORGE_ADMIN_KEY_SHA256=<hash>` and/or `QUERYFORGE_READER_KEY_SHA256=<hash>` in `.env`, then `docker compose up -d` |
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

If the record changed since it was read, the API returns `409 Conflict` with `"code": "concurrency_conflict"`. A missing or malformed `rowVersion` returns `400`.

## Running Tests

| Project | Type | Needs Docker |
|---|---|---|
| `QueryForge.Application.Tests` | Unit tests for application services and validation | No |
| `QueryForge.Api.Tests` | Unit tests for middleware and health response output | No |
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
- Do not execute user-provided SQL or dynamically generated database commands.
- Swagger is enabled in Development only. CORS origins come from `Cors:AllowedOrigins` (only `localhost:5173` in `appsettings.Development.json`); wildcards are rejected and non-Development origins must be HTTPS.
- API responses carry `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: default-src 'none'`, and `Cache-Control: no-store`; HSTS is enabled outside Development.
- API keys are shared secrets: there are no per-user identities, expiry, or self-service rotation. Treat each key like a password and prefer narrowly scoped `reader` keys.
- Integration tests generate random keys at runtime; no real or test credentials are committed.
