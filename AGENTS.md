
# QueryForge - Project Instructions

## Goal
Build a portfolio-quality API management platform
for a Senior .NET Engineer / Tech Lead portfolio.

## Technology
- Backend: ASP.NET Core Web API
- Frontend: React + TypeScript + Vite
- Database: SQL Server + Entity Framework Core
- Cache: Redis
- Tests: xUnit
- Local infrastructure: Docker Compose

## Architecture
- Start with a modular monolith.
- Keep controllers thin.
- Separate business logic from data access.
- Use dependency injection.
- Use async/await for I/O operations.
- Follow SOLID principles where appropriate.

## Engineering Rules
- Add tests for important business logic.
- Validate all external inputs.
- Use parameterized database queries.
- Never execute arbitrary user-provided SQL.
- Never hardcode secrets.
- Do not add dependencies without explaining why.
- Do not overwrite existing files without inspecting them.
- Run build and relevant tests after changes.

## Agent Workflow
1. Inspect the repository first.
2. Summarize the requirement and assumptions.
3. Propose an implementation plan.
4. Implement one logical feature at a time.
5. Run build and tests.
6. Fix errors introduced by the implementation.
7. Summarize changed files, test results and limitations.

Ask for approval before destructive operations,
production deployment, or changes outside the project.
