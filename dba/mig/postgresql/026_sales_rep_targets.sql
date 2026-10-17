-- Monthly sales-representative targets. Actual sales/commission remain derived from posted documents.
CREATE TABLE IF NOT EXISTS sales_rep_targets (
    target_id UUID NOT NULL PRIMARY KEY,
    company_id UUID NOT NULL,
    rep_id UUID NOT NULL,
    period_code VARCHAR(7) NOT NULL CHECK (period_code ~ '^[0-9]{4}-(0[1-9]|1[0-2])$'),
    target_amount NUMERIC(19,4) NOT NULL CHECK (target_amount >= 0),
    notes VARCHAR(500) NOT NULL DEFAULT '',
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL,
    created_by VARCHAR(255),
    modified_at TIMESTAMPTZ,
    modified_by VARCHAR(255),
    CONSTRAINT uq_sales_rep_targets_period UNIQUE (company_id, rep_id, period_code),
    CONSTRAINT fk_sales_rep_targets_company FOREIGN KEY (company_id) REFERENCES companies(company_id),
    CONSTRAINT fk_sales_rep_targets_rep FOREIGN KEY (rep_id) REFERENCES sales_representatives(rep_id)
);
CREATE INDEX IF NOT EXISTS ix_sales_rep_targets_company_period
    ON sales_rep_targets(company_id, period_code, is_deleted);
CREATE INDEX IF NOT EXISTS ix_sales_rep_targets_rep_period
    ON sales_rep_targets(rep_id, period_code, is_deleted);
