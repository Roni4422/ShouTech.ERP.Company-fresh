ALTER TABLE fiscal_years
    ADD COLUMN IF NOT EXISTS closed_by varchar(100),
    ADD COLUMN IF NOT EXISTS closed_at timestamptz;
