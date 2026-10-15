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
