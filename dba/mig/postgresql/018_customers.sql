-- Customers table for PostgreSQL
-- Part of ShouTech ERP Company database schema
-- Provider: PostgreSQL

CREATE TABLE IF NOT EXISTS customers (
    customer_id UUID NOT NULL PRIMARY KEY,
    company_id UUID NOT NULL,
    customer_code VARCHAR(50) NOT NULL,
    name_ar VARCHAR(200) NOT NULL,
    name_en VARCHAR(200) NOT NULL DEFAULT '',
    type INTEGER NOT NULL DEFAULT 1,
    tax_no VARCHAR(50) NOT NULL DEFAULT '',
    cr_no VARCHAR(50) NOT NULL DEFAULT '',
    credit_limit NUMERIC(19,4) NOT NULL DEFAULT 0,
    credit_days INTEGER NOT NULL DEFAULT 30,
    price_tier_id UUID,
    gl_account_id UUID,
    phone VARCHAR(30) NOT NULL DEFAULT '',
    mobile VARCHAR(30) NOT NULL DEFAULT '',
    email VARCHAR(100) NOT NULL DEFAULT '',
    address VARCHAR(500) NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT true,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    created_by UUID,
    modified_at TIMESTAMP WITH TIME ZONE,
    modified_by UUID,
    deleted_at TIMESTAMP WITH TIME ZONE,
    deleted_by UUID,
    
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

-- Function to update modified_at timestamp
CREATE OR REPLACE FUNCTION update_customers_modified_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.modified_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Trigger to update modified_at on update
CREATE TRIGGER trg_customers_updated 
BEFORE UPDATE ON customers
FOR EACH ROW
EXECUTE FUNCTION update_customers_modified_at();
