# Window review vs. Al-Ameen v10 / Phoenix v9

**Scope and limits.** Al-Ameen v10 and Phoenix v9 are proprietary products and were not available for hands-on
comparison; no claim of being "better" is made or can be verified from this repository. This is a code-level
audit of ShouTech windows against what a commercial accounting ERP is expected to provide.

## Findings (verified in source)
| Area | State |
|---|---|
| Invoice windows (`InvoiceWin`, 11 document kinds) | Post to party ledger + stock in one transaction; reversal with duplicate guard. **Gap:** no general-ledger journal entry on posting (no default-account settings exist). |
| Journal/cash vouchers | Balanced posting enforced in the database (migration 026). |
| Ribbon coverage | ~16 keys now open real reports (`QryWin`, migrations 027-029); most remaining keys have no backing tables. |
| POS (`PosWin`) | Window text already localized; status messages and the clear-sale confirmation were hardcoded English and are now localized (4 languages). |
| Remaining hardcoded UI text | `DatWin`, `StpWiz`, `CashDrawer`, `ComWin`, `CoaWin`, `BcdDesWin`, `MfgWin` (counts from a regex scan; unreviewed). |
| Automated UI tests | None; C# tests run only on Windows CI, which has not executed on this PR. |

## Priority gaps to close before any "better than" claim
1. Invoice posting -> balanced journal entries (needs default-account settings).
2. Localize remaining windows above.
3. Backing schema + windows for the unwired ribbon modules.
4. UI automation and manual UX comparison against the real products.
