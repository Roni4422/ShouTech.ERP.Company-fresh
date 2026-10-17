-- Migration: 021_sales_representatives
-- Description: Create sales_representatives table for managing sales representatives
-- Created: 2024

-- Sales Representatives table
CREATE TABLE IF NOT EXISTS sales_representatives (
    rep_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    rep_code TEXT NOT NULL,
    name TEXT NOT NULL,
    area TEXT NOT NULL DEFAULT '',
    commission_rate NUMERIC NOT NULL DEFAULT 0,
    phone TEXT NOT NULL DEFAULT '',
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    created_by TEXT,
    modified_at TEXT,
    modified_by TEXT,
    deleted_at TEXT,
    deleted_by TEXT,
    CONSTRAINT fk_sales_reps_company 
        FOREIGN KEY (company_id) REFERENCES companies(company_id),
    CONSTRAINT uq_sales_reps_code_company 
        UNIQUE (company_id, rep_code)
);

-- Indexes for performance
CREATE INDEX IF NOT EXISTS idx_sales_reps_company ON sales_representatives(company_id);
CREATE INDEX IF NOT EXISTS idx_sales_reps_code ON sales_representatives(rep_code);
CREATE INDEX IF NOT EXISTS idx_sales_reps_name ON sales_representatives(name);
CREATE INDEX IF NOT EXISTS idx_sales_reps_area ON sales_representatives(area);
CREATE INDEX IF NOT EXISTS idx_sales_reps_active ON sales_representatives(is_active);
CREATE INDEX IF NOT EXISTS idx_sales_reps_is_deleted ON sales_representatives(is_deleted);
CREATE INDEX IF NOT EXISTS idx_sales_reps_created_at ON sales_representatives(created_at);
CREATE INDEX IF NOT EXISTS idx_sales_reps_company_active ON sales_representatives(company_id, is_active, is_deleted);
