# Bogdabot

Bogdabot is a .NET backend for managing automated traders. It exposes a versioned
ASP.NET Core API and uses Akka.NET actors to manage trader lifecycles.

## Project layout

- `server/` — ASP.NET Core API, trading actors, and persistence.
- `shared/` — types shared by the backend projects.
- `cli/` — command-line project scaffold (currently prints “Hello, World!”).
- `bogdabot.slnx` — solution containing the server, tests, shared library, and CLI.

The main database stores users and trader metadata in PostgreSQL. Each trader's
orders and positions are stored in SQLite, either in its own database file or in
memory.

## Requirements

- .NET 10 SDK
- PostgreSQL for the main database

## Configuration

The server loads `.env.development` first and then `.env` from its current working
directory. Start it from `server/` so it finds the provided template:

```sh
cp .env.example .env
```

Edit `server/.env` and set the PostgreSQL connection values. For a local
PostgreSQL instance, `DB_MAIN_HOST` is usually `localhost`; the template's `db`
hostname is intended for a network where the database service is named `db`.
Create the configured database and ensure PostgreSQL is running before starting
the server. The server applies database migrations during startup.

| Variable | Purpose |
| --- | --- |
| `DB_MAIN_BACKEND` | Main database backend. Set to `PostgreSQL`. |
| `DB_MAIN_HOST`, `DB_MAIN_PORT` | PostgreSQL server address and port. |
| `DB_MAIN_DATABASE`, `DB_MAIN_USERNAME`, `DB_MAIN_PASSWORD` | PostgreSQL database and credentials. |
| `DB_TRADER_BACKEND` | Trader database backend. Set to `SQLite`. |
| `DB_TRADER_IN_MEMORY` | Set to `true` for in-memory trader databases or `false` for files. |
| `DB_TRADER_ROOT_FOLDER` | Directory for trader database files; required when not using in-memory databases. |
| `SUPERADMIN_EMAIL`, `SUPERADMIN_PASSWORD` | Optional initial superadmin account; leave empty to skip creating it. |
| `LOG_PATH` | Writable file path used for rolling logs in `Test` and `Production`. Development logs to the console. |

`ASPNETCORE_ENVIRONMENT` must be set in the process environment to `Development`,
`Test`, or `Production`; it is not read from the `.env` file. Keep local
credentials out of source control.

## Run the server

From the repository root:

```sh
cp server/.env.example server/.env
# Edit server/.env with your local PostgreSQL connection and other settings.
cd server
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project Bogdabot.Server.csproj
```

In Development, Swagger UI is available at `/swagger` on the server's local
address. Debug migration endpoints are also registered only in Development and
can destructively recreate database state; do not expose them to untrusted users.

## API

The API is versioned under `/api/v1`:

- `/api/v1/traders` — list and retrieve traders; create, start, stop, and delete
  traders.
- `/api/v1/stopped-traders` — list, retrieve, and delete stopped traders.

For request schemas and responses, use the Development Swagger UI.

## Build and test

Run these commands from the repository root:

```sh
dotnet build bogdabot.slnx
dotnet test server/tests/tests.csproj
```
