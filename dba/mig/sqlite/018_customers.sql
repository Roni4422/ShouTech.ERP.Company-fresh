-- Customers table for SQLite
-- Part of ShouTech ERP Company database schema
-- Provider: SQLite

CREATE TABLE IF NOT EXISTS customers (
    customer_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    customer_code TEXT NOT NULL,
    name_ar TEXT NOT NULL,
    name_en TEXT NOT NULL DEFAULT '',
    type INTEGER NOT NULL DEFAULT 1,
    tax_no TEXT NOT NULL DEFAULT '',
    cr_no TEXT NOT NULL DEFAULT '',
    credit_limit NUMERIC NOT NULL DEFAULT 0,
    credit_days INTEGER NOT NULL DEFAULT 30,
    price_tier_id TEXT,
    gl_account_id TEXT,
    phone TEXT NOT NULL DEFAULT '',
    mobile TEXT NOT NULL DEFAULT '',
    email TEXT NOT NULL DEFAULT '',
    address TEXT NOT NULL DEFAULT '',
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    created_by TEXT,
    modified_at TEXT,
    modified_by TEXT,
    deleted_at TEXT,
    deleted_by TEXT,
    
    CONSTRAINT fk_customers_company FOREIGN KEY (company_id) REFERENCES companies(company_id),
    CONSTRAINT fk_customers_gl_account FOREIGN KEY (gl_account_id) REFERENCES chart_of_accounts(account_id),
    CONSTRAINT uq_customers_code_company UNIQUE (company_id, customer_code)
);

-- Indexes for customers table
CREATE INDEX IF NOT EXISTS idx_customers_company ON customers(company_id);
CREATE INDEX IF NOT EXISTS idx_customers_code ON customers(customer_code);
CREATE INDEX IF NOT EXISTS idx_customers_name_ar ON customers(name_ar);
CREATE INDEX IF NOT EXISTS idx_customers_name_en ON customers(name_en);
CREATE INDEX IF NOT EXISTS idx_customers_type ON customers(type);
CREATE INDEX IF NOT EXISTS idx_customers_active ON customers(is_active);
CREATE INDEX IF NOT EXISTS idx_customers_deleted ON customers(is_deleted);
CREATE INDEX IF NOT EXISTS idx_customers_created_at ON customers(created_at);

-- Trigger to update modified_at on update
CREATE TRIGGER IF NOT EXISTS trg_customers_updated 
AFTER UPDATE ON customers
FOR EACH ROW
BEGIN
    UPDATE customers SET modified_at = datetime('now') WHERE customer_id = OLD.customer_id;
END;
