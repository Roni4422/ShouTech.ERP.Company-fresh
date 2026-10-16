# Window ↔ Database Field Persistence Audit

Branch: `main`
Scope: static source audit, first pass
Rule: a control bound in memory is **not** counted as persisted unless the save path writes it and the load path reads it back. A successful build is not a field round-trip test.

## Status definitions

- **Blocked**: save/post is deliberately rejected because there is no persistence implementation in the window.
- **Repository path exists**: the window calls a repository, but this does not prove every field/child row is mapped correctly.
- **Runtime round-trip pending**: save → close/reopen → compare every scalar field and child-grid row has not yet been demonstrated.

## High-priority data-entry windows

| Window | Entity / intended data | DB/repository path observed | Static finding | Field + child-row round-trip |
|---|---|---|---|---|
| `CstCard.cs` | Customers | No customer repository call in window | **Blocked**; save/delete report unavailable | Not passed |
| `BnkCard.cs` | Banks | No bank repository call in window | **Blocked**; save/delete report unavailable | Not passed |
| `CcCard.cs` | Cost centers | No cost-center repository call in window | **Blocked**; save/delete report unavailable | Not passed |
| `RepCard.cs` | Sales representatives | No representative repository call in window | **Blocked**; save/delete report unavailable | Not passed |
| `CurCard.cs` | Currencies / exchange rates | No currency repository call in window | **Blocked**; save reports unavailable | Not passed; exchange-rate rows need explicit coverage |
| `CshWin.cs` | Receipt/payment voucher + allocation lines | No voucher repository call in window | **Blocked**; save/post report unavailable | Not passed; header and every allocation row pending |
| `JrnWin.cs` | Journal voucher + journal lines | No journal repository call in window | **Blocked**; save/post report unavailable | Not passed; header and every debit/credit line pending |
| `ChqWin.cs` | Cheque batch + cheque rows | No cheque repository call in window | **Blocked**; save reports unavailable | Not passed; batch and every cheque row pending |
| `OrdWin.cs` | Sales/purchase order + item rows | No order repository call in window | **Blocked**; save is rejected and conversion now explicitly reports unavailable instead of opening an empty invoice (commit `ace64e2`) | Not passed |
| `MfgWin.cs` | Manufacturing order + item rows | No manufacturing repository call in window | **Blocked**; save/post report unavailable | Not passed |
| `InvoiceWin.cs` | Sales/purchase invoices and returns | `InvRepo.SaveDraft`, `InvRepo.Load`, `InvRepo.Post`, reversal path; material lookup via `MatRepo` | Real persistence path exists | Not proven for every header field and line field; round-trip pending |
| `PosCheckoutWin.cs` | POS sale | `InvRepo.SaveDraft`, `InvRepo.Post` | Real invoice persistence path exists | Tender/payment metadata and all invoice rows need verification |
| `AccCard.xaml.cs` | Chart-of-accounts card | `AccRepo.LoadAll`, `AccRepo.Save`, `AccRepo.Delete` | Real repository path exists | Every account field and parent/child behavior need round-trip tests |
| `MatCard.xaml.cs` | Material card, units/prices | `MatRepo.LoadAll`, `LoadById`, `Save`, `SoftDelete`; lookup helpers | Real repository path exists | Every material field plus all units/prices/barcodes need round-trip tests |
| `CatWin.xaml.cs` | Material groups/categories | `MatRepo.LoadGroups`, `SaveGroup`, `SoftDeleteGroup` | Real repository path exists | Every group field and parent relationship need round-trip tests |
| `WhsWin.xaml.cs` | Warehouses | `MatRepo.LoadWarehouses`, `SaveWarehouse`, `SoftDeleteWarehouse` | Real repository path exists | Every warehouse field and parent relationship need round-trip tests |

## Initial conclusions

1. **The audit cannot certify that every field is saved yet.** Static repository calls alone do not prove complete field mapping.
2. The ten blocked windows above do not currently provide a real database save path from their UI. Their forms must not be treated as production-ready data-entry screens.
3. `InvoiceWin`, `PosCheckoutWin`, `AccCard`, `MatCard`, `CatWin`, and `WhsWin` have repository paths, but require field-by-field verification against the actual SQLite and PostgreSQL schema.
4. Each document grid must be tested as parent + child records: save, reopen, compare every field and row count, edit one field, save again, reopen, and compare again. Posting must also be checked for atomicity and ledger/stock effects.
5. Reports, setup/security/backup/import-export windows, and the remaining master-data/document windows are not yet covered in this first pass. The complete inventory is still in progress; this file must not be read as a completed all-window certification.

## Required test matrix for each persistent form

- Record every input control and its backing property.
- Trace property → repository parameter/column → schema column.
- Trace schema column → repository mapper → input control on reload.
- Include nullable/optional fields, decimal precision, dates, checkboxes, combo-box foreign keys, and localized text.
- Include every DataGrid child row and add/remove/edit cases.
- Verify company/tenant scope, transaction boundaries, and error logging.
- Run SQLite and PostgreSQL tests where both providers are supported.


## Additional windows checked (second pass)

| Window | Observed data path | Static result | Round-trip |
|---|---|---|---|
| `CoaWin.xaml.cs` | `AccRepo.EnsureSchema`, `AccRepo.LoadAll`, `AccRepo.Save`, account import/export | Real account repository path; inspect every account property and import mapping | Not passed |
| `PosWin.cs` | `MatRepo.LoadById` and inventory/unit lookups; final checkout delegated to invoice workflow | Sales workspace itself is not the final persistence boundary; checkout and payment metadata need tracing | Not passed |
| `MatDirWin.xaml.cs` | `MatRepo.LoadAll`, `LoadGroups`, `SoftDelete` | Directory/search and delete use repository reads; verify delete confirmation/audit and all entry points | Not passed |
| `BcdGenWin.cs` | `MatRepo.LoadAll`, `LoadById(includeChildren: true)`, `MatRepo.Save` | Real material repository path. Commit `85d9e69` removes the unconditional success dialog: it now displays the saved/failed summary with an outcome-appropriate icon and logs per-material exceptions. | Not passed; generated barcode still needs reload verification |
| `SerWin.xaml.cs` | `MatRepo.LoadAll`; saves search-window preferences separately | Search dialog payload is a selected material, not a business-record save form | Not applicable to material persistence; preference round-trip not tested |
| `BkpWin.xaml.cs` / `RestoreCoWin.xaml.cs` | Backup/restore service workflow | These operate on backup packages/companies rather than ordinary form-row CRUD; require separate restore integrity tests | Not passed |
| `BarWin.cs` / `BcdDesWin.cs` | Barcode printing/design; design is documented as local configuration | Printing is not DB persistence; template/config persistence must be distinguished from business data | Not passed |

## Repository/schema cross-check observations

- `Inventory.cs` exposes real `MatRepo.LoadAll`, `LoadById`, `Save`, `SoftDelete`, `SaveWarehouse`, and group operations. The presence of these methods does not by itself prove that every `MatRec` field and each child table (extra barcodes, price list, suppliers, stock-related rows, units) is fully mapped in both directions.
- `InvRepo.cs` exposes `SaveDraft` and `Load`, with separate SQLite/PostgreSQL migration files for commercial documents. The invoice header and every line/tax/discount/payment-related field still need an explicit mapping comparison and database round-trip test.
- `Accounting.cs` exposes `AccRepo.LoadAll`, `Save`, and `Delete`; account-card and chart import paths need tests for all account properties, parent/child renumbering, and provider parity.
- The current PostgreSQL migration files `003_modules.sql` and `004_modules.sql` were fetched from `main` and have identical content (6,735 bytes and the same blob SHA). This is a migration-chain duplication risk that should be resolved only after checking the migration runner's applied-history compatibility; do not simply delete a migration that may already be recorded in deployed databases.
- The historical `docs/SCHEMA_AUDIT_PHASE1.md` explicitly marks itself superseded. Its 540/120/55 counts are historical, not a fresh current count. A fresh `tools/schema_audit.py` run and the strict mode should be run in CI before claiming that no service DDL remains.

## Next verification order

1. Audit each repository record type against SQL columns and its write/read mappers: `MatRec`, `WarehouseRec`, `MatGroupRec`, `AccRec`, `InvDoc` and `InvLine`.
2. Add automated integration tests using temporary SQLite databases for full create/read/update/re-read and child-row add/edit/remove; then run equivalent PostgreSQL tests.
3. Build a machine-readable per-window field matrix from controls and repository mappings. Mark a field **PASS** only after the persisted value is read back and compared; mark unsupported fields **BLOCKED**, never assume success.
4. Finish inventory of remaining `cod/app/src/Views/**` windows (reports, administration, import/export, company/user/security, configuration and document modules). Those windows are not certified by this partial report.


## Third-pass correction (2026-10-16)

- `BcdGenWin.SaveGenerated()` previously showed `Messages.SaveSuccess` even when `saved == 0` and all selected writes failed. Fixed on `main` in commit `85d9e69d47c74cede31e8075e2225cda73699983`: the dialog now shows the actual saved/failed summary and uses warning/error icons for partial/total failure; individual persistence exceptions are sent to `AppHost.Logging.LogError` with the material ID.
- This is a source-level correction only. It has **not** been runtime-tested here; barcode persistence must still be proven by reloading each changed material from the database. This audit remains partial and does not certify all windows or fields.

- `OrdWin.ConvertToInvoice()` was also corrected in commit `ace64e241bc38a65ff4cf26b883b7e7df20b1015`: the previous handler opened a blank invoice without transferring or persisting the order. It now reports the operation as unavailable, avoiding a misleading conversion workflow. Actual order persistence and conversion remain unimplemented.
