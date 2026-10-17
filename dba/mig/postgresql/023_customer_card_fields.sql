-- Additive customer-card fields. Existing rows and foreign-key columns are preserved.
ALTER TABLE customers ADD COLUMN IF NOT EXISTS group_code TEXT NOT NULL DEFAULT '';
ALTER TABLE customers ADD COLUMN IF NOT EXISTS price_tier_code TEXT NOT NULL DEFAULT '';
ALTER TABLE customers ADD COLUMN IF NOT EXISTS gl_account_code TEXT NOT NULL DEFAULT '';
