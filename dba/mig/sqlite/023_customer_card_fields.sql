-- Additive customer-card fields. Existing rows and foreign-key columns are preserved.
-- @if-column-missing customers.group_code
ALTER TABLE customers ADD COLUMN group_code TEXT NOT NULL DEFAULT '';
-- @if-column-missing customers.price_tier_code
ALTER TABLE customers ADD COLUMN price_tier_code TEXT NOT NULL DEFAULT '';
-- @if-column-missing customers.gl_account_code
ALTER TABLE customers ADD COLUMN gl_account_code TEXT NOT NULL DEFAULT '';
