-- Banks table for SQLite
-- Part of ShouTech ERP Company database schema
-- Provider: SQLite

CREATE TABLE IF NOT EXISTS banks (
    bank_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    bank_code TEXT NOT NULL,
    name TEXT NOT NULL,
    type INTEGER NOT NULL DEFAULT 1,
    gl_account_id TEXT,
    account_number TEXT NOT NULL DEFAULT '',
    address TEXT NOT NULL DEFAULT '',
    phone TEXT NOT NULL DEFAULT '',
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    created_by TEXT,
    modified_at TEXT,
    modified_by TEXT,
    deleted_at TEXT,
    deleted_by TEXT,
    
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
