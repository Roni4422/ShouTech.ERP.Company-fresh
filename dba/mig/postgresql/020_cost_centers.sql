-- Cost Centers table for PostgreSQL
-- Part of ShouTech ERP Company database schema
-- Provider: PostgreSQL

CREATE TABLE IF NOT EXISTS cost_centers (
    cost_center_id UUID NOT NULL PRIMARY KEY,
    company_id UUID NOT NULL,
    cost_center_code VARCHAR(50) NOT NULL,
    name_ar VARCHAR(200) NOT NULL,
    name_en VARCHAR(200) NOT NULL DEFAULT '',
    description VARCHAR(500) NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT true,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    created_by UUID,
    modified_at TIMESTAMP WITH TIME ZONE,
    modified_by UUID,
    deleted_at TIMESTAMP WITH TIME ZONE,
    deleted_by UUID,
    
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
