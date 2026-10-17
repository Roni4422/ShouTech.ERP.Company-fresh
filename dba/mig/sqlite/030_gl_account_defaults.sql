-- Migration: 030_gl_account_defaults
-- Description: Per-company default GL accounts used when commercial documents (sales/purchase
-- invoices and returns) post journal entries. Roles: AR, AP, SALES, SALES_RETURN, TAX_OUT,
-- TAX_IN, INVENTORY, COGS. Until a company configures at least one role, document posting
-- does not create journal entries (legacy behavior); a partial configuration blocks posting.
CREATE TABLE IF NOT EXISTS gl_account_defaults (
    company_id TEXT NOT NULL,
    role TEXT NOT NULL CHECK (role IN ('AR','AP','SALES','SALES_RETURN','TAX_OUT','TAX_IN','INVENTORY','COGS')),
    account_id TEXT NOT NULL,
    updated_by TEXT,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (company_id, role),
    FOREIGN KEY (company_id) REFERENCES companies(company_id),
    FOREIGN KEY (account_id) REFERENCES chart_of_accounts(account_id)
);
