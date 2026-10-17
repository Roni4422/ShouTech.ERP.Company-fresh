# Chart of Accounts, Fiscal Periods, and Year-End Controls

## Invoice posting to the general ledger

Posting a sales or purchase document now also creates a posted journal entry, in the same database transaction as the document status, party ledger and stock movements (`GlPost.PostDocument`, called from `InvRepo.Post`). The entry is linked to the document through its document number `GL:<kind>:<document no>` and its reference number.

| Document | Debit | Credit |
| --- | --- | --- |
| Sales invoice | Receivables; cost of goods sold | Sales revenue; output tax; inventory |
| POS sale | Cash; cost of goods sold | Sales revenue; output tax; inventory |
| Sales return | Sales returns (or sales revenue); output tax; inventory | Receivables; cost of goods sold |
| Purchase invoice | Inventory; input tax | Payables |
| Purchase return | Payables | Inventory; input tax |

- **Account mappings:** the `account_mappings` table maps `RECEIVABLES`, `PAYABLES`, `CASH`, `BANK`, `SALES_REVENUE`, `SALES_RETURNS`, `OUTPUT_TAX`, `INPUT_TAX`, `INVENTORY` and `COST_OF_GOODS_SOLD` to postable company accounts. Migration 029 expands the mapping constraint to include `BANK` and adds `sales_documents.payment_method`. POS cash sales debit `CASH`, Visa/K-Net debit `BANK`, and credit sales debit `RECEIVABLES`; credit sales with a selected customer also update the party ledger. Configure all mappings required by a document before posting; missing required mappings reject the posting and keep the document in draft. `SALES_RETURNS` remains optional and falls back to `SALES_REVENUE`.
- **Cost of goods:** sales and sales returns use the current average cost in `material_stock_balances` for the line's warehouse (any warehouse when none is set), taken before the stock movement is applied.
- **Currency:** ledger amounts are in the company base currency: the document amount multiplied by the document exchange rate. The journal header keeps the document currency code and rate.
- **Fiscal periods:** the document date must fall in an open period of an open fiscal year, otherwise posting is refused. Returns and invoice reversals post their own mirror entries, so closed-period corrections follow the same rules as any other entry.
- **Not covered yet:** per-material-group revenue accounts, discounts booked to a separate account, and settlement (receipts and payments against open invoices).

## Financial statements and closing entry

- `GetTrialBalanceAsync(companyId, from, to)` returns opening balance, period debit/credit and closing balance per account, plus an `IsBalanced` flag.
- `GetIncomeStatementAsync(companyId, from, to)` returns revenue and expense lines and net income.
- `GetBalanceSheetAsync(companyId, asOf)` returns assets, liabilities and equity. Revenue and expense activity not yet closed is shown as current earnings, so the sheet balances before and after closing.
- All three read the `v_posted_gl_lines` view (migration 022), which counts posted and reversed entries together so a reversal pair nets to zero. Drafts never count.
- `PostYearEndClosingEntryAsync` clears every revenue/expense balance for the year into the supplied EQUITY account and posts the entry in the last open period. Run it before closing the final period; it returns null when there is nothing to close.
- The year-close revenue/expense check now counts reversed entries too (status 2), so a reversed entry no longer blocks year closure.

## Scope

This phase adds a provider-neutral accounting structure for SQLite and PostgreSQL. It does not delete existing business records. Legacy journal rows are assigned to generated monthly periods where their dates fall within a fiscal year.

## Chart of accounts

`chart_of_accounts` stores company-scoped account codes, names, account types, optional parent accounts, and active/deleted/postable flags. Account types are `ASSET`, `LIABILITY`, `EQUITY`, `REVENUE`, and `EXPENSE`. Parent accounts must be non-postable. Journal draft creation now checks that every account exists in the same company and is active, not deleted, and postable. Database posting triggers repeat this check to protect against code paths that bypass the repository.

Before posting journals in an existing database, import or create the company's chart of accounts and map legacy account IDs to the new `account_id` values. The migration intentionally does not invent account definitions from opaque legacy IDs.

## Fiscal years and periods

A fiscal year has explicit start/end dates and a closed flag. Each new fiscal year created by `AccountingStructureRepository.CreateFiscalYearAsync` receives twelve monthly periods. A journal draft is assigned to the single open period that matches its company and date. Posting is rejected when no period matches, the period is closed/locked, or the fiscal year is closed.

Existing fiscal years are retained. For legacy journal history, migration 009/011 creates fiscal-year/period records for years represented by journal entries and attaches matching entries to periods. If the organization uses a non-calendar fiscal year, review and correct the dates before posting new entries; do not assume calendar years fit every company.

## Closing a period

Call `AccountingStructureRepository.ClosePeriodAsync(periodId, actor)`. The method:

- refuses closure while draft entries remain;
- checks that posted/reversed entries in the period balance;
- requires periods to close in chronological order;
- records the closing actor and timestamp;
- only transitions `OPEN` to `CLOSED`.

The database trigger independently prevents a journal from being posted into a closed period or closed year.

## Closing a fiscal year

Call `AccountingStructureRepository.CloseFiscalYearAsync(fiscalYearId, actor)` only after all twelve periods are `CLOSED` or `LOCKED`. It refuses closure if drafts remain, posted journals are unbalanced, or any active revenue/expense account retains a non-zero net balance. This last check requires an approved income-statement closing journal to have been posted first; the repository never guesses a retained-earnings account, so the caller supplies it. `PostYearEndClosingEntryAsync(fiscalYearId, retainedEarningsAccountId, actor)` creates and posts that closing journal.

A closed fiscal year cannot accept new journal postings. There is intentionally no automatic reopen method; reopening requires a separately authorized and audited workflow.

## Al-Ameen reference adaptation

The design follows the useful accounting patterns seen in the supplied Al-Ameen SQL references: account code/name lookup, account hierarchy, unit/period-aware balances, and posted-document semantics. SQL Server stored procedures were treated as business-rule references only and were not copied into active SQLite/PostgreSQL migrations.

## Deployment checks

1. Back up each company database before applying migrations.
2. Apply migrations in the existing migration runner, not by manually replaying individual SQL files.
3. Populate/migrate the chart of accounts and verify all legacy journal account IDs.
4. Verify fiscal-year start/end dates and period mappings for each company.
5. Run the trial balance and reconcile it before closing periods or years.
6. Test PostgreSQL against a real supported server before production use; SQLite-only smoke tests do not certify PostgreSQL behavior.
