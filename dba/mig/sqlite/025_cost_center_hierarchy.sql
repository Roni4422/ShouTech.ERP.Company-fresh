-- Persist cost-center hierarchy fields exposed by the cost-center card.
-- @if-column-missing cost_centers.parent_code
ALTER TABLE cost_centers ADD COLUMN parent_code TEXT NOT NULL DEFAULT '';
-- @if-column-missing cost_centers.level_no
ALTER TABLE cost_centers ADD COLUMN level_no INTEGER NOT NULL DEFAULT 1;
