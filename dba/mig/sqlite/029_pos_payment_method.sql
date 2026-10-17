-- Persist the selected POS tender so posting can route the debit to the correct mapped account.
-- @if-column-missing sales_documents.payment_method
ALTER TABLE sales_documents ADD COLUMN payment_method TEXT NOT NULL DEFAULT 'Cash';

-- SQLite cannot alter a CHECK constraint; rebuild the mapping table to permit BANK.
CREATE TABLE account_mappings_new (
    company_id TEXT NOT NULL,
    mapping_key TEXT NOT NULL CHECK (mapping_key IN (
        'RECEIVABLES','PAYABLES','CASH','BANK','SALES_REVENUE','SALES_RETURNS',
        'OUTPUT_TAX','INPUT_TAX','INVENTORY','COST_OF_GOODS_SOLD')),
    account_id TEXT NOT NULL,
    updated_by TEXT,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (company_id, mapping_key),
    FOREIGN KEY(company_id) REFERENCES companies(company_id),
    FOREIGN KEY(account_id) REFERENCES chart_of_accounts(account_id)
);
INSERT INTO account_mappings_new(company_id, mapping_key, account_id, updated_by, updated_at)
SELECT company_id, mapping_key, account_id, updated_by, updated_at FROM account_mappings;
DROP TABLE account_mappings;
ALTER TABLE account_mappings_new RENAME TO account_mappings;
CREATE INDEX IF NOT EXISTS ix_account_mappings_account ON account_mappings(account_id);
