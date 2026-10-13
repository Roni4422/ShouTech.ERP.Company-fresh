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

## PostgreSQL migration review

`003_modules.sql` and `004_modules.sql` currently contain identical SQL, while `003_rbac.sql` is a separate RBAC seed migration. The migrator identifies migrations by their full relative file path, not by the numeric prefix alone, so `003_modules.sql` and `003_rbac.sql` are distinct migration IDs. Equal numeric prefixes are confusing but are not themselves a primary-key collision.

Do not delete, rename, or rewrite these historical files as a cleanup-only change. A deployed database may have recorded any of them in `schema_migrations`, and the migrator verifies checksums for applied migrations that are still present on disk. Keep historical files intact until there is an explicit compatibility plan for every supported deployment. For fresh installations, a future cleanup can be handled by a reviewed baseline/squash strategy or a documented migration policy; do not simply replace the old chain in place.

The current branch preserves the original migration files. The duplicate module SQL is still a technical debt item, not considered fixed. Before changing this chain, test a fresh PostgreSQL database and a disposable copy of an existing database, and compare their `schema_migrations` records. Do not manually edit migration history to hide checksum or history discrepancies.

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
