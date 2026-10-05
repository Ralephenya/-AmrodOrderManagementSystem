# Amrod Order Management (SADC)

A small full-stack order management system for customers in the Southern African Development Community.
It's built for the Amrod Intermediate Full-Stack Developer technical assessment.

> 🚧 Work in progress. See [docs/PLAN.md](docs/PLAN.md) for the build plan and progress log.

## Stack

| Area | Choice |
|---|---|
| API | ASP.NET Core (.NET 8), controllers, FluentValidation, ErrorOr, ProblemDetails |
| Data | SQL Server, EF Core code-first (writes, migrations) and Dapper (reports) |
| Messaging | RabbitMQ via MassTransit 8 (transactional outbox, inbox dedup, retry, dead-letter) |
| Auth | Microsoft Entra JWT via Microsoft.Identity.Web (mock issuer for local/dev) |
| Frontend | React + TypeScript (Vite), TanStack Query, client typed from OpenAPI |
| Orchestration | .NET Aspire (local dev), Docker Compose (containers) |
| Testing | xUnit, Shouldly, WebApplicationFactory, Testcontainers, Vitest + RTL, Playwright |
| CI/CD | GitHub Actions |

## Getting started

See [docs/ONBOARDING.md](docs/ONBOARDING.md) *(coming soon)*.

## Repository layout

```
src/        backend projects (AppHost, ServiceDefaults, Domain, Infrastructure, Api, Worker, Contracts)
web/        React + TypeScript frontend
tests/      unit and integration tests
db/         generated SQL migration scripts
deploy/     Docker Compose and Dockerfiles
docs/       plan, onboarding, architecture notes
```

## Sections to come

- Architecture and data flow
- EF Core migrations: commands, SQL scripts, zero-downtime plan, rollback strategy
- SADC currencies and Common Monetary Area notes
- Written answers: [ANSWERS.md](ANSWERS.md)
