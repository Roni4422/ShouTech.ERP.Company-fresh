-- Maps business events to chart-of-accounts accounts so commercial documents can post to the general ledger.
-- A company with no rows here keeps the legacy behaviour (no general-ledger posting from invoices).
CREATE TABLE IF NOT EXISTS account_mappings (
    company_id uuid NOT NULL REFERENCES companies(company_id),
    mapping_key varchar(40) NOT NULL CHECK (mapping_key IN (
        'RECEIVABLES','PAYABLES','CASH','SALES_REVENUE','SALES_RETURNS',
        'OUTPUT_TAX','INPUT_TAX','INVENTORY','COST_OF_GOODS_SOLD')),
    account_id uuid NOT NULL REFERENCES chart_of_accounts(account_id),
    updated_by varchar(100),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, mapping_key)
);
CREATE INDEX IF NOT EXISTS ix_account_mappings_account ON account_mappings(account_id);
