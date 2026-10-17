-- Store the legal operating country used for company-specific tax defaults.
-- @if-column-missing companies.country
ALTER TABLE companies ADD COLUMN country TEXT;
