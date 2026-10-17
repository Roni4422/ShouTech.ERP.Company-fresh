-- Additive bank-card fields; preserve existing bank and account data.
-- @if-column-missing banks.branch_name
ALTER TABLE banks ADD COLUMN branch_name TEXT NOT NULL DEFAULT '';
-- @if-column-missing banks.iban
ALTER TABLE banks ADD COLUMN iban TEXT NOT NULL DEFAULT '';
-- @if-column-missing banks.currency_code
ALTER TABLE banks ADD COLUMN currency_code TEXT NOT NULL DEFAULT 'SAR';
-- @if-column-missing banks.gl_account_code
ALTER TABLE banks ADD COLUMN gl_account_code TEXT NOT NULL DEFAULT '';
