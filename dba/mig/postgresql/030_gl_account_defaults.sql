-- Migration: 030_gl_account_defaults
-- Description: Per-company default GL accounts used when commercial documents (sales/purchase
-- invoices and returns) post journal entries. Roles: AR, AP, SALES, SALES_RETURN, TAX_OUT,
-- TAX_IN, INVENTORY, COGS. Until a company configures at least one role, document posting
-- does not create journal entries (legacy behavior); a partial configuration blocks posting.
CREATE TABLE IF NOT EXISTS gl_account_defaults (
    company_id UUID NOT NULL,
    role VARCHAR(20) NOT NULL CHECK (role IN ('AR','AP','SALES','SALES_RETURN','TAX_OUT','TAX_IN','INVENTORY','COGS')),
    account_id UUID NOT NULL,
    updated_by VARCHAR(100),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (company_id, role),
    FOREIGN KEY (company_id) REFERENCES companies(company_id),
    FOREIGN KEY (account_id) REFERENCES chart_of_accounts(account_id)
);
