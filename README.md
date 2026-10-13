# ShouTech ERP — Company

ShouTech ERP Company is the company-level application within the ShouTech ERP project.

## Start here

- [Developer guide](README.dev.md) — supported providers, configuration, build/run guidance, and development rules.
- [Database migrations](docs/DATABASE_MIGRATIONS.md) — migration ordering, checksums, safe upgrades, and compatibility requirements.
- [Documentation index](docs/) — project design and operational notes.

## Supported databases

The active database providers are SQLite and PostgreSQL. SQL Server scripts in archived reference material are not part of the active runtime.

## Before changing the database

1. Read the migration guide.
2. Inspect pending migrations with the migrator's `--dry-run` option.
3. Test the full chain against a fresh disposable database and an upgrade copy representing an existing installation.
4. Back up existing databases before applying migrations.
5. Never edit or delete an applied migration merely to make a fresh installation cleaner. Prefer a forward migration and document any compatibility behavior.

## Contributions

Keep changes scoped and reviewable. Run the applicable build, tests, and database migration checks before merging. Do not commit credentials, production data, or local package caches.
