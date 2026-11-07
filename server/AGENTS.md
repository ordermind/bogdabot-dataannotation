# Bogdabot Backend Agent Guide

## Purpose

This file provides high-signal repository context for AI coding agents working in this backend.

## Tech Stack

- .NET 10 (net10.0)
- ASP.NET Core Web API
- Akka.NET actors
- EF Core + FluentMigrator
- PostgreSQL for main/shared data
- SQLite for trader-specific data
- Serilog logging
- xUnit v3 on Microsoft.Testing.Platform

## Architecture

- The project follows a DDD-style layout with contexts directly under src:
    - Account
    - Infrastructure
    - Shared
    - Trading
    - Web
- Common patterns:
    - Each context wires dependencies in Bootstrap/*ServiceLoader classes implementing IServiceLoader.
    - Startup actions and one-time initialization are handled by Bootstrap/*Bootstrapper classes implementing IBootstrapper.

## Startup Flow

- Entry point: Program.cs
- Service registration order:
    1.  SharedServiceLoader
    2.  InfrastructureServiceLoader
    3.  AccountServiceLoader
    4.  TradingServiceLoader
    5.  WebServiceLoader
- Bootstrapper execution order:
    1.  SharedBootstrapper
    2.  InfrastructureBootstrapper
    3.  AccountBootstrapper
    4.  TradingBootstrapper
    5.  WebBootstrapper

This order matters when introducing new dependencies or startup side effects.

## API Conventions

- API routes are versioned under /api/v1.
- Debug endpoints exist under /api/v1/debug/* and include migrate-fresh actions.
- Debug controllers are development-only:
    - Program.cs removes them outside Development using ExcludeControllerFeatureProvider.

## Database Model

- Main database:
    - Backend type controlled by DB_MAIN_BACKEND.
    - Current supported value is PostgreSQL.
    - Main migrations run during infrastructure bootstrap via IMainDbMigrationService.RunMigrations().
- Trader databases:
    - Backend type controlled by DB_TRADER_BACKEND.
    - Current supported value is SQLite.
    - Each trader actor uses its own SQLite file unless DB_TRADER_IN_MEMORY=true.
    - On-disk location format: {DB_TRADER_ROOT_FOLDER}/user-{userGuid}/{traderActorId}.db.
    - Trader migrations run in TraderActor initialization using ITraderDbMigrationService.RunMigrations(...).

## Actor System Notes

- ActorHostService starts akka-universe as a hosted service.
- Root actors created on startup:
    - trader-supervisor
    - web
- Trader lifecycle orchestration is centralized in Trading/Actors/TraderSupervisor.cs:
    - create, start, stop, delete, and termination handling.
    - actor termination behavior is intentionally stateful (pending stop vs pending deletion).

## Config And Environment

- Environment variables are loaded by SharedServiceLoader through EnvironmentLoader.
- Use .env.example as the template for required variables.
- Do not hardcode secrets in code or tests.
- LOG_PATH controls file logging path for Serilog.

## Build, Run, Test

- Build:
    - dotnet build /application/bogdabot-backend.csproj
- Run (watch mode):
    - dotnet watch run --project /application/bogdabot-backend.csproj
- Tests:
    - dotnet test /application/tests/tests.csproj
    - This repo uses Microsoft.Testing.Platform style filtering after --.
    - Example class filter:
        - dotnet test /application/tests/tests.csproj -- --filter-class Namespace.ClassName

## Testing Expectations

- Unit tests should cover all meaningful code paths of the tested method. For unit tests, no actor system or framework should be involved, and also no database queries should be executed.
- Integration tests may include the actor system, framework, and database queries. All queries should be done in-memory.
- Behavior tests should make real api calls, test the output and compare it to internal state such as database records.
- Keep tests close to current structure under tests/Unit, tests/Integration, and tests/Behavior.
- For new behavior in actors, migrations, or API validation, add or update focused tests.
- Do not wait for longer than 10 seconds when running tests.

## Editing Guidelines For Agents

- Preserve DDD boundaries; avoid leaking infrastructure concerns into domain-focused contexts.
- Register new services in the relevant context ServiceLoader, not ad hoc in random files.
- Put startup side effects (migrations, initialization) in the relevant Bootstrapper.
- Follow existing async/await patterns and dependency injection style.
- Do not edit generated build output under artifacts/ unless explicitly asked.

## High-Value Files

- Program.cs: composition root and environment-specific controller filtering.
- src/Infrastructure/InfrastructureServiceLoader.cs: infrastructure DI composition.
- src/Infrastructure/InfrastructureBootstrapper.cs: infrastructure startup flow.
- src/Infrastructure/Akka/ActorHostService.cs: actor system bootstrap.
- src/Trading/Actors/TraderSupervisor.cs: trader actor lifecycle logic.
- src/Infrastructure/Postgres/Bootstrap/PostgresServiceLoader.cs: main DB + identity setup.
- src/Infrastructure/Sqlite/Config/SqliteDatabaseConfigFactory.cs: trader DB path and mode logic.
- src/Web/Bootstrap/WebServiceLoader.cs: controller/json/swagger configuration.

## Communication rules

- Always communicate in English.
