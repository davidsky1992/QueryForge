# QueryForge

QueryForge is a portfolio-oriented API management platform built as a modular monolith.

## Stack

- ASP.NET Core Web API on .NET 8
- Entity Framework Core with SQL Server
- Redis distributed cache wiring
- React + TypeScript + Vite
- xUnit tests
- Docker Compose for local SQL Server and Redis

## Local Development

Credentials are never stored in tracked files. Docker Compose reads the SQL Server password from a git-ignored `.env` file, and the API reads its connection string from .NET user secrets (stored in your user profile, outside the repository).

Create your local `.env` and replace the placeholder with a strong password:

```powershell
Copy-Item .env.example .env
notepad .env
```

Start infrastructure only:

```powershell
docker compose up -d sqlserver redis
```

> The SA password is applied only when the SQL Server volume is first created. If you change it later, either keep using the original password or recreate the volume with `docker compose down -v` (this deletes local data).

Store the API connection string as a user secret, using the same password as `.env`:

```powershell
dotnet user-secrets set "ConnectionStrings:SqlServer" "Server=localhost,1433;Database=QueryForge;User Id=sa;Password=<your-password>;TrustServerCertificate=True;Encrypt=False" --project src/QueryForge.Api
```

Outside Development, supply it through the `ConnectionStrings__SqlServer` environment variable or a secret store instead.

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

Open Swagger at the API `/swagger` endpoint and the Vite app at `http://localhost:5173`.

## Health Checks

| Endpoint | Purpose | Status codes |
|---|---|---|
| `GET /health/live` | Liveness: the process is responding; no dependencies are checked. | `200` |
| `GET /health` | Readiness: SQL Server and Redis are checked independently (3 s timeout each). | `200` Healthy/Degraded, `503` Unhealthy |

SQL Server is required, so its failure makes the API `Unhealthy`. Redis only backs the optional distributed cache, so its failure reports `Degraded`. Responses contain status, a generic description, and duration per check; exception messages and connection details are never returned.

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

Any failing step fails the workflow. The workflow has read-only repository permissions and uses no secrets. In CI (`CI=true`), [Directory.Build.props](Directory.Build.props) treats all warnings — including NuGet vulnerability warnings — as errors. To reproduce locally:

```powershell
$env:CI = "true"; dotnet build QueryForge.sln -c Release; Remove-Item Env:CI
```

[global.json](global.json) pins the .NET 8.0.4xx SDK feature band (rolling forward to newer feature bands).

## Windows 8 GB RAM Notes

- Keep SQL Server and Redis in Docker, but run the API and frontend directly on Windows.
- SQL Server is capped to `2g` in Compose and Redis is capped to `256m`.
- Avoid running full-stack containers during normal development.
- Prefer `dotnet watch` and Vite HMR over rebuilding containers.

## Security Notes

- Never commit `.env` or real connection strings; `.env.example` contains placeholders only.
- The `sa` login and `TrustServerCertificate=True` are acceptable for local development only.
- Do not store third-party API credentials in API definition records.
- Do not execute user-provided SQL or dynamically generated database commands.
- Swagger and permissive local CORS are intended for development only.
