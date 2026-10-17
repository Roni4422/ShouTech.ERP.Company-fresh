-- Monthly sales-representative targets. Actual sales/commission remain derived from posted documents.
CREATE TABLE IF NOT EXISTS sales_rep_targets (
    target_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    rep_id TEXT NOT NULL,
    period_code TEXT NOT NULL,
    target_amount NUMERIC NOT NULL CHECK (target_amount >= 0),
    notes TEXT NOT NULL DEFAULT '',
    is_deleted INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    created_by TEXT,
    modified_at TEXT,
    modified_by TEXT,
    CONSTRAINT uq_sales_rep_targets_period UNIQUE (company_id, rep_id, period_code),
    CONSTRAINT fk_sales_rep_targets_company FOREIGN KEY (company_id) REFERENCES companies(company_id),
    CONSTRAINT fk_sales_rep_targets_rep FOREIGN KEY (rep_id) REFERENCES sales_representatives(rep_id),
    CONSTRAINT ck_sales_rep_targets_period CHECK (length(period_code) = 7)
);
CREATE INDEX IF NOT EXISTS ix_sales_rep_targets_company_period
    ON sales_rep_targets(company_id, period_code, is_deleted);
CREATE INDEX IF NOT EXISTS ix_sales_rep_targets_rep_period
    ON sales_rep_targets(rep_id, period_code, is_deleted);
