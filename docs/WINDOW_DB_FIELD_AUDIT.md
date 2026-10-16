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
| `OrdWin.cs` | Sales/purchase order + item rows | No order repository call in window | **Blocked**; save reports unavailable; conversion opens an invoice window but does not prove order persistence | Not passed |
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
