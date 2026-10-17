# Database Migrations

ShouTech supports only SQLite and PostgreSQL. SQL Server, Oracle, and MySQL are not supported providers.

- SQLite migrations: `dba/mig/sqlite/`
- PostgreSQL migrations: `dba/mig/postgresql/`
- Nested migration folders are discovered recursively.
- Applied migrations are recorded in `schema_migrations` with SHA-256 checksums. Applied migration files are immutable; add a new migration instead of editing an applied file.
- Each migration runs in its own transaction.
- Existing databases without migration history are not modified automatically. Verify their schema and use `--baseline-existing` only after taking a backup and reviewing the migration list.

Examples:

```powershell
dotnet run --project tools/SqlMigrator -- --provider sqlite --connection "Data Source=./shoutech_erp.sqlite3" --dry-run
dotnet run --project tools/SqlMigrator -- --provider sqlite --connection "Data Source=./shoutech_erp.sqlite3"
dotnet run --project tools/SqlMigrator -- --provider postgresql --connection "Host=localhost;Database=shoutech;Username=...;Password=..." --dry-run
```

Connection strings can be passed through `SHOUTECH_CONNECTION_STRING` or `SHOUTECH_DB_CONNECTION`; select the provider with `SHOUTECH_DB_PROVIDER`.

## Background outbox

The migration-managed `outbound_messages` table is available on both providers. The Windows service reads `SHOUTECH_DB_CONNECTION` (or `SHOUTECH_CONNECTION_STRING`) and `SHOUTECH_DB_PROVIDER` (`SQLite` by default or `PostgreSQL`). Only HTTP/HTTPS webhook dispatch is implemented; email/SMS require explicit connectors. Queue rows should be inserted by application code through a parameterized outbox API and should not be edited manually in production.

The Al-Ameen SQL files are archived reference material. Their inventory and posting behavior can inform requirements, but SQL Server procedures must not be run against SQLite/PostgreSQL.

## 023_company_features

Adds `company_features` (per-company enterprise feature flags such as audit trail, multi-currency, multi-branch and approval workflow). Matching UI strings live under the `Enterprise` section of `Ar/En/Fr/Tr.json`.

## 027 report views
`027_report_views.sql` (SQLite + PostgreSQL) defines `v_rpt_negative_stock`, `v_rpt_stagnant_stock`, `v_rpt_cash_journal`, `v_rpt_profit_loss` and `v_rpt_sales_by_party`. They back `QryWin`, which `RptNav` opens from MWin ribbon keys `Inventory.NegativeOutput`, `Inventory.StagnantMaterials`, `Bonds.CashJournal`, `Accounts.ProfitLoss`, `Inventory.SalesReport` and `Customers.CustomerReports`. Migration `028_report_views_2.sql` adds `v_rpt_daily_journal`, `v_rpt_dormant_accounts`, `v_rpt_cost_center_balance` and `v_rpt_active_materials` (ribbon keys `Accounts.DailyReport`, `Accounts.DormantAccounts`, `Accounts.CostCenterBalance`/`CostCenterReport`/`CostCenter`, `Inventory.MostActiveMaterials`/`MaterialStatistics`). Add new ribbon reports by adding a view migration and one `QryWin` definition.

## 029 invoice report views
`029_invoice_report_views.sql` adds `v_rpt_invoice_profit` (revenue vs. current average cost), `v_rpt_quotations` and `v_rpt_packing_list`, opened by `QryWin` from `Invoices.InvoiceProfits`, `Invoices.SearchQuotations` and `Invoices.PackingList`. Profit uses current average cost, not historical cost at sale time.
