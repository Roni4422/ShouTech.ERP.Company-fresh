-- Additive bank-card fields; preserve existing bank and account data.
ALTER TABLE banks ADD COLUMN IF NOT EXISTS branch_name TEXT NOT NULL DEFAULT '';
ALTER TABLE banks ADD COLUMN IF NOT EXISTS iban TEXT NOT NULL DEFAULT '';
ALTER TABLE banks ADD COLUMN IF NOT EXISTS currency_code TEXT NOT NULL DEFAULT 'SAR';
ALTER TABLE banks ADD COLUMN IF NOT EXISTS gl_account_code TEXT NOT NULL DEFAULT '';
