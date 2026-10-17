-- Migration: 021_sales_representatives
-- Description: Create sales_representatives table for managing sales representatives
-- Created: 2024

-- Sales Representatives table
CREATE TABLE IF NOT EXISTS sales_representatives (
    rep_id UUID NOT NULL PRIMARY KEY,
    company_id UUID NOT NULL,
    rep_code VARCHAR(50) NOT NULL,
    name VARCHAR(200) NOT NULL,
    area VARCHAR(100) NOT NULL DEFAULT '',
    commission_rate DECIMAL(5,2) NOT NULL DEFAULT 0,
    phone VARCHAR(30) NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT true,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    created_by VARCHAR(255),
    modified_at TIMESTAMP WITH TIME ZONE,
    modified_by VARCHAR(255),
    deleted_at TIMESTAMP WITH TIME ZONE,
    deleted_by VARCHAR(255),
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
