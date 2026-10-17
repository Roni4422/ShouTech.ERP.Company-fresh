-- Maps business events to chart-of-accounts accounts so commercial documents can post to the general ledger.
-- A company with no rows here keeps the legacy behaviour (no general-ledger posting from invoices).
CREATE TABLE IF NOT EXISTS account_mappings (
    company_id TEXT NOT NULL,
    mapping_key TEXT NOT NULL CHECK (mapping_key IN (
        'RECEIVABLES','PAYABLES','CASH','SALES_REVENUE','SALES_RETURNS',
        'OUTPUT_TAX','INPUT_TAX','INVENTORY','COST_OF_GOODS_SOLD')),
    account_id TEXT NOT NULL,
    updated_by TEXT,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (company_id, mapping_key),
    FOREIGN KEY(company_id) REFERENCES companies(company_id),
    FOREIGN KEY(account_id) REFERENCES chart_of_accounts(account_id)
);
CREATE INDEX IF NOT EXISTS ix_account_mappings_account ON account_mappings(account_id);
