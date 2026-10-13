# Excel Import Rewrite and SQL Migration Audit

## Scope and status

This audit covers the active Excel import/export service, the import window, the SQL migration runner, and active SQLite/PostgreSQL migration files. Legacy snapshots and archived SQL Server scripts are not treated as executable migrations.

The work is being developed on `feature/excel-import-sql-audit-clean`, based on the latest `main`. No production database was accessed or modified.

## Excel import findings

### Confirmed defects and limitations

1. `DataExportSvc.ReadExcelSheet` previously opened `xl/worksheets/sheet1.xml` directly. XLSX worksheets are referenced by `xl/workbook.xml` and `xl/_rels/workbook.xml.rels`; the first worksheet is not guaranteed to be stored as `sheet1.xml`.
2. The old import path rejected every workbook that did not contain exactly one encrypted ShouTech dataset identity marker. This prevented importing ordinary XLSX templates.
3. The reader previously passed numeric date cells through as raw numbers and did not use cell styles to identify Excel date serials.
4. Invalid date strings could be returned as strings by the parser instead of failing validation at the row that contains the invalid value.
5. Replace mode was callable for datasets beyond master data. That could allow a spreadsheet operation to soft-delete rows in transaction datasets when the table exposed a soft-delete column. Posted financial documents must not be removed by a generic data exchange operation.

### Changes in this branch so far

- Resolve the first worksheet through workbook relationships instead of assuming `sheet1.xml`.
- Read shared strings, inline strings, booleans, formula error cells, and date-formatted numeric cells.
- Accept generic `.xlsx` files only when the user-selected dataset is allow-listed and each header maps unambiguously to a real database column. Database column names are used as headers for this generic path.
- Infer generic import types from SQLite/PostgreSQL column metadata; reject unknown headers and duplicate headers.
- Keep the complete primary-key and company-scope checks, row validation, parameterized values, and single transaction.
- Reject invalid date values rather than silently passing them through.
- Restrict replace mode to a fixed list of master-data dataset keys. Sales, purchases, returns, journals, and other transaction datasets are excluded.

### Remaining Excel work before calling this complete

- Added regression tests with generated XLSX workbooks for relationship-based worksheet selection, shared strings, inline strings, Boolean cells, date serials, formula-error rejection, integer-backed Boolean inference, and blocking transactional datasets.
- Still add database-backed tests for blank/sparse cells, duplicate headers, generic header mapping, primary-key omission, company scoping, rollback on row failure, and replace-mode restrictions.
- Added a transaction-backed preview/dry-run before commit: the importer runs the real constraint-checked writes and replace cleanup inside a transaction, rolls the transaction back, then displays row counts and requires explicit confirmation. The committed import is bound to the SHA-256 hash of the workbook shown in the preview so a changed file is rejected before writing. Database-backed automated tests for preview rollback and row-level diagnostics remain outstanding.
- Consider an explicit mapping UI for translated/custom header names. The current generic path intentionally requires actual database field names so it does not guess a mapping.
- Run the Windows/.NET 8 build and test workflow. A code edit is not considered verified until CI completes successfully.

## SQL migration audit

### 1. Duplicate PostgreSQL migration

`dba/mig/postgresql/003_modules.sql` and `dba/mig/postgresql/004_modules.sql` have identical contents. The second file repeats the same module DDL and adds no schema capability on a fresh database.

The migration runner uses the full relative path as the migration ID, not only the numeric prefix. Therefore `003_modules.sql` and `003_rbac.sql` do not collide in `schema_migrations`; however, equal prefixes are confusing and the duplicated module migration is unnecessary history.

**Safe handling:** do not delete or rewrite either historical migration in place. Existing databases may have recorded either file and the runner validates checksums for applied migrations still present on disk. Keep the history intact until a tested compatibility/squash policy exists.

### 2. Multiple schema sources of truth

The migration chain is not the only place defining the runtime schema:

- The active SQLite and PostgreSQL core migrations define a `products` table.
- The runtime inventory service creates and uses `materials` and related material tables.
- The export catalog resolves both names as candidates, and other runtime C# services create additional tables dynamically.
- Legacy monolithic snapshots (`dba/lite.sql` and `dba/pg.sql`) contain overlapping table definitions but are not the active migration chain.

This is not proof that every duplicate is an executable migration conflict. It is evidence that fresh database initialization can depend on which bootstrap path ran. The project needs a schema ownership matrix: each runtime table must have one canonical definition and a documented creation/upgrade path for both SQLite and PostgreSQL.

### 3. Tenant identifier conventions are mixed

The core migration models `companies.company_id` as a UUID (PostgreSQL) or text identifier (SQLite). Legacy tenant tables can instead store the human-readable company code in a column named `company_id`. The import service therefore contains a table-name allow-list to decide which convention applies.

This mixed convention is fragile: adding a new table without updating the list can lead to a mismatch between the active company code and the internal company ID. The previous runtime error about company code `001` not being registered is consistent with a database/bootstrap mismatch, although that specific incident must be diagnosed against its actual database and migration history.

**Safe handling:** document the identifier type per table and move the convention into explicit table metadata or a canonical schema; do not infer it from a column name alone. Any conversion needs data-preserving migrations and tests for both providers.

### 4. Migration ordering and baselining

The migrator sorts relative migration paths lexicographically, case-insensitively. Numeric prefixes are conventions, not parsed semantic versions. Its `--baseline-existing` option records the discovered migration files without executing them; it cannot prove that the existing database has the schema those files describe.

**Safe handling:** only baseline after a schema comparison and backup. Add integration tests for an empty database, an already migrated database, a checksum mismatch, a partially initialized database, and a pre-existing database being baselined. The runner should eventually report historical migration IDs that exist in `schema_migrations` but no longer exist on disk.

### 5. Permission catalog drift

A static comparison of the permission seed entries found 228 distinct permission codes in SQLite `004_permissions.sql` and 227 in PostgreSQL `006_permissions.sql`. The missing PostgreSQL entry was `SYS.PERMISSIONS.MANAGE`, which is referenced by `PrmWin.xaml.cs` and the permission constants in `Permissions.cs`. SQLite seeds it and grants it to active administrator roles; PostgreSQL did not seed it in the active permission migration.

A forward-only migration, `dba/mig/postgresql/007_permission_manage.sql`, now upserts this permission and grants it only to active administrator roles. It deliberately does not grant it to User or Viewer roles. This is a targeted correction; the full catalogs and role grants still need an automated cross-provider parity test.

### 5.1. Company ID resolution and natural keys

Internal-ID-scoped imports now fail closed when the `companies` registry is absent or empty, instead of substituting the company code for a UUID/integer ID. Explicitly classified legacy tables whose `company_id` column stores the company code bypass this lookup; global tables do not need a company lookup.

The importer no longer requires generated surrogate IDs when a complete unique key is available in the workbook. For example, `materials` has a company-scoped unique key on `(company_id, code)`, allowing a file with `code` and `name` to insert/update a material without an exported `material_id`. The UPSERT names the conflict target explicitly for PostgreSQL compatibility. Blank generated IDs are omitted from INSERT statements and primary keys are never included in the UPDATE set.

### 5.2. Runtime-created material schema

The `materials` table and its related material-card tables are created by the runtime schema in `cod/app/src/Services/Inventory.cs`, not by the active `dba/mig/sqlite/` migration chain. The active migrations define `products`, while the data-exchange catalog prefers `materials` when it exists and falls back to `products` otherwise. Therefore, the selected “Products” dataset can point to different physical schemas depending on whether inventory initialization has run. This is a real source of header/primary-key mismatches and must be covered by fresh-database initialization tests; the SQLite migration-chain test alone does not exercise runtime-created material tables.

For `materials`, the database column is `name_ar` and it is `NOT NULL`. The importer accepts common spreadsheet headers such as `name`, `الاسم`, `اسم المادة`, and `اسم الصنف` as aliases for that existing column. It does not rename the database schema or weaken the constraint. If blank material names should be allowed, that requires a deliberate schema and UI validation change across both providers, not only an Excel header alias.

### 6. Cross-provider drift

SQLite and PostgreSQL do not have identical migration decomposition: SQLite core defines 14 tables, while PostgreSQL splits the equivalent core/security set across `001_core.sql` and `002_security.sql`. PostgreSQL additionally defines 19 module tables in `003_modules.sql`; there is no matching SQLite module migration in the active SQLite migration directory. This is a confirmed capability/schema parity gap if those modules are expected to work on SQLite.

A shared table-name list alone is not sufficient; column types, primary keys, foreign keys, unique constraints, and tenant scope must also be compared. The legacy monolithic snapshots are not a safe substitute for missing active migrations.

The migration runner now recognizes only the known PostgreSQL `003_modules.sql` / `004_modules.sql` duplicate pair and records the second ID without re-running the identical module DDL. It does not suppress other identical SQL files automatically. Both historical IDs and checksums remain intact; the redundant file stays present until a future migration-baseline policy can retire it safely.

### 7. Additional importer hardening

The current importer now separates legacy company-code-scoped tables from tables that require an internal company ID. Legacy tables do not query the global company registry merely to obtain a value they store as a code. Tables that require an internal ID fail with a specific initialization error if the registry exists but is empty; the importer does not guess an ID.

Generic master-data imports now prefer a complete primary key when present, but can use a database-enforced unique constraint when the workbook omits generated IDs. For example, the materials dataset can use the company-scoped `(company_id, code)` key instead of requiring an auto-generated `material_id`. PostgreSQL and SQLite UPSERT statements specify the chosen conflict target explicitly; PostgreSQL requires a conflict target for `DO UPDATE`. Blank generated primary-key fields are omitted rather than inserted as NULL. For materials, common `name` / Arabic name headers map to the existing `name_ar` column without weakening its database constraint.

The XLSX reader also rejects formula cells (including cached formula results), duplicate cell coordinates, populated values beyond the header width, formula-error cells, packages above 64 MiB uncompressed, worksheets above 32 MiB, shared-string tables above 24 MiB, workbooks above 100,000 data rows, and rows wider than 512 columns. These limits reduce ambiguous imports and memory-exhaustion risk from malformed workbooks. Date-format detection ignores quoted literals, color/locale brackets, and escaped characters while retaining elapsed-time formats such as `[h]:mm:ss`.

Regression tests cover formula rejection, extra data columns, legacy company-scope classification, custom date-format detection, natural-key selection without generated IDs, material-name aliases, empty replace workbooks, and the PostgreSQL permission migration file. Database-backed tests for empty-company registries and rollback behavior are still required.

## Verification checklist

- [ ] Build `tests/Kern.Tests/Kern.Tests.csproj` on Windows/.NET 8 (including the latest transaction-backed preview changes).
- [ ] Run the existing test suite and add import-specific XLSX tests.
- [ ] Run `SqlMigrator --dry-run` for SQLite and PostgreSQL and record the exact ordered IDs.
- [ ] Apply migrations to disposable empty SQLite and PostgreSQL databases. A new automated test now executes the entire active SQLite migration chain in an empty in-memory database; the PostgreSQL chain still needs a disposable server-backed test.
- [ ] Compare expected tables, columns, primary keys, foreign keys, unique constraints, and permission codes across both providers.
- [ ] Test baseline behavior against a disposable existing database and inspect `schema_migrations`.
- [ ] Verify posted invoices, vouchers, journal entries, and inventory movements cannot be imported, overwritten, or removed by spreadsheet or encrypted dataset-package operations.

## References

- [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader)
- [Microsoft Open XML SDK](https://github.com/dotnet/Open-XML-SDK)
- Repository schema inventory: `docs/SCHEMA_AUDIT_PHASE1.md`
- Migration policy: `docs/DATABASE_MIGRATIONS.md`
