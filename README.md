# ShouTech ERP

ShouTech ERP is a Windows desktop ERP application with shared domain and application libraries, a background service, and database migration tooling. The active runtime supports SQLite and PostgreSQL.

## Technology

- C# and .NET 8; the desktop application uses WPF and targets Windows.
- SQLite for local and single-user use, or PostgreSQL for server and multi-user deployments.
- C++ sources and build configuration under `sec/`.
- Python support files under `int/`.

## Repository structure

- `cod/app/` — WPF desktop application.
- `src/` — domain, application, and infrastructure libraries.
- `svc/` — Windows background service.
- `tools/SqlMigrator/` — database migration runner.
- `dba/mig/sqlite/` and `dba/mig/postgresql/` — provider-specific SQL migrations.
- `tests/Kern.Tests/` — .NET tests.
- `docs/` — project, architecture, and database documentation.
- `cfg/` — application configuration defaults.
- `sec/`, `int/` — security and AI-related supporting components.

## Prerequisites

- .NET 8 SDK.
- Windows for building and running the WPF desktop application and Windows service.
- A SQLite database file or a PostgreSQL server for database-backed use.
- CMake and a C++20 compiler only if working with the C++ component.
- Python 3 only if working with the Python component.

## Build and test

From the repository root on Windows:

```powershell
dotnet restore ShouTech.sln
dotnet build ShouTech.sln
dotnet test tests/Kern.Tests/Kern.Tests.csproj
```

## Database migrations

Review the migration guide before changing or baselining an existing database. To inspect and then apply SQLite migrations:

```powershell
dotnet run --project tools/SqlMigrator -- --provider sqlite --connection "Data Source=./shoutech_erp.sqlite3" --dry-run
dotnet run --project tools/SqlMigrator -- --provider sqlite --connection "Data Source=./shoutech_erp.sqlite3"
```

For PostgreSQL, use `--provider postgresql` and supply a PostgreSQL connection string. Keep credentials out of source control. The migrator records applied migration IDs and checksums; see [Database Migrations](docs/DATABASE_MIGRATIONS.md) before using `--baseline-existing`.

## Further reading

- [Development guide](README.dev.md)
- [Documentation index and project guides](docs/SHOUTECH_ERP_EN.md)
- [Database migration guide](docs/DATABASE_MIGRATIONS.md)
