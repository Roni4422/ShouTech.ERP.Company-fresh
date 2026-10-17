-- Persist cost-center hierarchy fields exposed by the cost-center card.
ALTER TABLE cost_centers ADD COLUMN IF NOT EXISTS parent_code TEXT NOT NULL DEFAULT '';
ALTER TABLE cost_centers ADD COLUMN IF NOT EXISTS level_no INTEGER NOT NULL DEFAULT 1;
CREATE INDEX IF NOT EXISTS ix_cost_centers_parent_level
    ON cost_centers(company_id, parent_code, level_no);
