# Database Migrations

ShouTech supports only SQLite and PostgreSQL. SQL Server, Oracle, and MySQL are not supported providers.

- SQLite migrations: `dba/mig/sqlite/`
- PostgreSQL migrations: `dba/mig/postgresql/`
- Nested migration folders are discovered recursively.
- Applied migrations are recorded in `schema_migrations` with SHA-256 checksums.
- Each migration runs in its own transaction.
- Existing databases without migration history are not modified automatically. Verify their schema and use `--baseline-existing` only after taking a backup and reviewing the migration list.

## Ordering and immutability

The current `SqlMigrator` identifies a migration by its relative path under the provider migration directory and sorts those paths lexicographically, case-insensitively. It does not parse a numeric prefix as a semantic version. Use unique, consistently zero-padded prefixes and descriptive names so the execution order is obvious.

Treat an applied migration as immutable. If schema or seed data needs to change, add a new migration. Never edit an applied file to fix an old migration. Before renaming or deleting a migration, review migration history from supported deployed databases and define a compatibility plan. The current runner verifies checksums for discovered applied migrations; it does not report every historical migration file that is now missing from disk, so a deleted file can otherwise go unnoticed.

## PostgreSQL duplicate-file cleanup

The previous `003_modules.sql` and `004_modules.sql` contained identical SQL (the same Git blob). The duplicate has been removed from the proposed cleanup branch so fresh databases do not execute the same schema declarations twice.

The RBAC seed migration was moved from `003_rbac.sql` to `005_rbac.sql` to give it a distinct, later path prefix after the module schema migration. Its permission inserts use `ON CONFLICT DO NOTHING`, and the role-permission insert also ignores conflicts, making a rerun idempotent for those seed records.

Compatibility considerations:

- A database that already recorded `003_rbac.sql` will see `005_rbac.sql` as a new migration and may execute the RBAC seed statements again. Those statements are designed to be idempotent, but test this against a disposable copy before deployment.
- A database that previously recorded `004_modules.sql` will retain that history row. Removing the duplicate file does not erase database history.
- The module migration uses `CREATE SCHEMA IF NOT EXISTS` and `CREATE TABLE IF NOT EXISTS`; still, validate foreign-key prerequisites and the complete migration chain on an empty PostgreSQL database before merging.
- Do not manually edit `schema_migrations` to hide checksum or history discrepancies.

This cleanup should be merged only after the repository's migration checks pass against both an empty PostgreSQL database and a representative existing database. Do not run this branch against production before that validation.

## Examples

Inspect SQLite migrations without applying them:

```powershell
dotnet run --project tools/SqlMigrator -- --provider sqlite --connection "Data Source=./shoutech_erp.sqlite3" --dry-run
```

Apply SQLite migrations to a disposable or backed-up database:

```powershell
dotnet run --project tools/SqlMigrator -- --provider sqlite --connection "Data Source=./shoutech_erp.sqlite3"
```

Inspect PostgreSQL migrations without applying them:

```powershell
dotnet run --project tools/SqlMigrator -- --provider postgresql --connection "Host=localhost;Database=shoutech;Username=...;Password=..." --dry-run
```

Connection strings can be passed through `SHOUTECH_CONNECTION_STRING` or `SHOUTECH_DB_CONNECTION`; select the provider with `SHOUTECH_DB_PROVIDER`.

## Background outbox

The migration-managed `outbound_messages` table is available on both providers. The Windows service reads `SHOUTECH_DB_CONNECTION` (or `SHOUTECH_CONNECTION_STRING`) and `SHOUTECH_DB_PROVIDER` (`SQLite` by default or `PostgreSQL`). Only HTTP/HTTPS webhook dispatch is implemented; email/SMS require explicit connectors. Queue rows should be inserted by application code through a parameterized outbox API and should not be edited manually in production.

The Al-Ameen SQL files are archived reference material. Their inventory and posting behavior can inform requirements, but SQL Server procedures must not be run against SQLite/PostgreSQL.
