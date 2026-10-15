# Database Schema Ownership Audit (main)

## Goal

The canonical definitions of application tables must live under `dba/` (preferably provider-specific, ordered SQL migrations). C# files under `cod/app/src/Services/` should contain runtime operations and migration orchestration, not embedded `CREATE TABLE` definitions. SQL in the legacy SQL Server archive is reference material and is not a deployable SQLite/PostgreSQL schema.

## Audit result

The existing `docs/SCHEMA_AUDIT_PHASE1.md` reports 540 SQL table-definition occurrences, 120 repeated table names, and 55 runtime C# `CREATE TABLE` statements. A direct review of the current `main` branch confirms that table DDL is embedded in multiple service files and some definitions are duplicated within a single service.

### Confirmed service files containing table/schema DDL

| File | Findings |
|---|---|
| `cod/app/src/Services/Accounting.cs` | `accounts` table defined twice (SQLite/PostgreSQL variants); creates indexes; adds columns dynamically via `ALTER TABLE`. |
| `cod/app/src/Services/Data.cs` | Creates core tables including `companies`, `users`, `fiscal_years`, `roles`, `permissions`, `products`, journal tables, stock tables, and permission tables; also creates indexes and adds columns dynamically. Several tables already exist in migrations or legacy schema snapshots. |
| `cod/app/src/Services/DocSchema.cs` | Embeds separate SQLite and PostgreSQL DDL strings for `sales_documents`, `sales_document_lines`, `purchase_documents`, `purchase_document_lines`, `party_ledger`, and `stock_movements`, plus indexes. |
| `cod/app/src/Services/InvRepo.cs` | Defines `document_reversals` separately for the two providers. |
| `cod/app/src/Services/Inventory.cs` | Creates `materials`, material child tables, stock tables, `material_groups`, `material_group_sequences`, and `warehouses`; several table definitions are repeated for provider variants; also performs dynamic column additions and creates indexes. |
| `cod/app/src/Services/RecycleBinSvc.cs` | Creates `deletion_audit_log` at runtime. |
| `tools/SqlMigrator/Program.cs` | Creates the migration-history infrastructure table `schema_migrations`. This is infrastructure DDL; if the ownership rule is absolute, it too must be moved to the DBA bootstrap SQL while preserving first-run behavior. |

A repository-wide search also found schema snapshots in `dba/lite.sql` and `dba/pg.sql`, older scripts under `dba/`, and active migrations under `dba/mig/sqlite/` and `dba/mig/postgresql/`. The archived SQL Server scripts under `archive/legacy_sqlserver/` must remain clearly separated from deployable migrations.

## Implementation progress (2026-10-15)

The first centralization pass has been committed directly to `main`:

- `DocSchema.cs`: embedded SQLite/PostgreSQL table DDL removed; now validates the DBA-managed commercial-document schema.
- `InvRepo.cs`: inline creation of `document_reversals` removed; now validates its presence.
- `RecycleBinSvc.cs`: inline creation of `deletion_audit_log` removed; table presence is validated before audit operations. Provider-specific audit-trigger installation remains in the service and is a separate schema-ownership follow-up.
- `Accounting.cs`: inline account-table DDL and its runtime column bootstrap removed.
- `Inventory.cs`: inline DDL for materials, child tables, material groups, group sequences, and warehouses removed. The old warehouse compatibility path that could drop a legacy `warehouses` table was removed to avoid data loss. The service now validates required tables.
- `Data.cs`: inline core-table bootstrap and development-only table-drop reset removed; permission-table creation and runtime permission-column additions were moved to DBA migrations.
- Added provider-specific migrations for commercial documents/reversals, audit storage, accounts, inventory, groups/warehouses, and the missing SQLite category/brand tables. SQLite migrations now support the explicit `-- @if-column-missing table.column` directive for safe conditional column additions to existing databases.

This is a significant first pass, not a claim that the entire repository has been audited and validated end-to-end. The PostgreSQL migration numbering/content issue documented below remains unresolved. GitHub Actions builds triggered during this pass initially reported compile errors; fixes were committed, and the latest CI run must complete before treating the code as build-verified. Fresh SQLite/PostgreSQL migration execution and representative existing-database upgrade tests are still required.

## Important migration defect found

In `dba/mig/postgresql/`, `003_modules.sql` and `004_modules.sql` have identical Git blob hashes and identical content. Also, `003_rbac.sql` shares the `003` filename prefix with `003_modules.sql`. The current migrator derives each migration ID from the path relative to the provider migration directory and sorts those IDs; duplicate content and unclear ordering require correction.

**Do not simply delete or rename already-released migration files.** Existing databases record migration IDs and checksums. Before repairing the migration sequence, the migrator needs a compatibility plan for already-recorded IDs and tests for both fresh and previously migrated databases.

## Required refactor plan

1. Treat the current main-branch code as the baseline and preserve it in Git history.
2. Consolidate every supported SQLite/PostgreSQL table definition into ordered SQL files under `dba/mig/sqlite/` and `dba/mig/postgresql/`. Keep provider-specific syntax separate.
3. Preserve data and behavior: translate each runtime `CREATE TABLE`, index, and column-addition path into appropriate migrations; do not merely remove runtime SQL.
4. Refactor services to execute parameterized CRUD/business operations only. They must not own table definitions. Schema upgrades should be run through one migration path.
5. Resolve existing duplicate schema sources (`dba/lite.sql`, `dba/pg.sql`, older root-level SQL scripts, and migrations) by documenting which are active, legacy, or generated. Do not delete compatibility files until references and deployment workflows are checked.
6. Repair migration identity/order compatibly. Existing applied migration files remain immutable; introduce forward migrations or a deliberate compatibility mechanism instead of rewriting checksummed migrations.
7. Add a static audit/test that scans non-archive C# source for table DDL and fails when new schema definitions are added outside `dba/`.
8. Validate: clean SQLite creation, clean PostgreSQL creation, upgrade of representative existing databases, application startup, company initialization, inventory/document flows, permissions, and accounting/reversal behavior.

## Completion criteria

The refactor is complete only when all supported table-creation DDL is under `dba/`, service C# contains no table-creation definitions (except any explicitly documented bootstrap exception that is also moved into DBA scripts), migration IDs are unique and compatible, and the build plus database integration tests pass for both supported providers.

This document records the audit findings and safe execution order; it does not claim that the full refactor or integration tests have already been completed.
