-- Persist the selected POS tender so posting can route the debit to the correct mapped account.
ALTER TABLE sales_documents
    ADD COLUMN IF NOT EXISTS payment_method varchar(30) NOT NULL DEFAULT 'Cash';

ALTER TABLE account_mappings
    DROP CONSTRAINT IF EXISTS account_mappings_mapping_key_check;

ALTER TABLE account_mappings
    ADD CONSTRAINT account_mappings_mapping_key_check
    CHECK (mapping_key IN (
        'RECEIVABLES','PAYABLES','CASH','BANK','SALES_REVENUE','SALES_RETURNS',
        'OUTPUT_TAX','INPUT_TAX','INVENTORY','COST_OF_GOODS_SOLD'));
