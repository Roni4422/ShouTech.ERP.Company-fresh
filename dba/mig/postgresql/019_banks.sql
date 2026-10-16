-- Banks table for PostgreSQL
-- Part of ShouTech ERP Company database schema
-- Provider: PostgreSQL

CREATE TABLE IF NOT EXISTS banks (
    bank_id UUID NOT NULL PRIMARY KEY,
    company_id UUID NOT NULL,
    bank_code VARCHAR(50) NOT NULL,
    name VARCHAR(200) NOT NULL,
    type INTEGER NOT NULL DEFAULT 1,
    gl_account_id UUID,
    account_number VARCHAR(100) NOT NULL DEFAULT '',
    address VARCHAR(500) NOT NULL DEFAULT '',
    phone VARCHAR(30) NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT true,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    created_by UUID,
    modified_at TIMESTAMP WITH TIME ZONE,
    modified_by UUID,
    deleted_at TIMESTAMP WITH TIME ZONE,
    deleted_by UUID,
    
    CONSTRAINT fk_banks_company FOREIGN KEY (company_id) REFERENCES companies(company_id),
    CONSTRAINT fk_banks_gl_account FOREIGN KEY (gl_account_id) REFERENCES chart_of_accounts(account_id),
    CONSTRAINT uq_banks_code_company UNIQUE (company_id, bank_code)
);

-- Indexes for banks table
CREATE INDEX IF NOT EXISTS idx_banks_company ON banks(company_id);
CREATE INDEX IF NOT EXISTS idx_banks_code ON banks(bank_code);
CREATE INDEX IF NOT EXISTS idx_banks_name ON banks(name);
CREATE INDEX IF NOT EXISTS idx_banks_type ON banks(type);
CREATE INDEX IF NOT EXISTS idx_banks_active ON banks(is_active);
CREATE INDEX IF NOT EXISTS idx_banks_deleted ON banks(is_deleted);
CREATE INDEX IF NOT EXISTS idx_banks_created_at ON banks(created_at);
