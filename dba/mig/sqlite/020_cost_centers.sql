-- Cost Centers table for SQLite
-- Part of ShouTech ERP Company database schema
-- Provider: SQLite

CREATE TABLE IF NOT EXISTS cost_centers (
    cost_center_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    cost_center_code TEXT NOT NULL,
    name_ar TEXT NOT NULL,
    name_en TEXT NOT NULL DEFAULT '',
    description TEXT NOT NULL DEFAULT '',
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    created_by TEXT,
    modified_at TEXT,
    modified_by TEXT,
    deleted_at TEXT,
    deleted_by TEXT,
    
    CONSTRAINT fk_cost_centers_company FOREIGN KEY (company_id) REFERENCES companies(company_id),
    CONSTRAINT uq_cost_centers_code_company UNIQUE (company_id, cost_center_code)
);

-- Indexes for cost_centers table
CREATE INDEX IF NOT EXISTS idx_cost_centers_company ON cost_centers(company_id);
CREATE INDEX IF NOT EXISTS idx_cost_centers_code ON cost_centers(cost_center_code);
CREATE INDEX IF NOT EXISTS idx_cost_centers_name_ar ON cost_centers(name_ar);
CREATE INDEX IF NOT EXISTS idx_cost_centers_name_en ON cost_centers(name_en);
CREATE INDEX IF NOT EXISTS idx_cost_centers_active ON cost_centers(is_active);
CREATE INDEX IF NOT EXISTS idx_cost_centers_deleted ON cost_centers(is_deleted);
CREATE INDEX IF NOT EXISTS idx_cost_centers_created_at ON cost_centers(created_at);
