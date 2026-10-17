-- Migration: 023_company_features
-- Description: Per-company enterprise feature flags (audit trail, multi-currency, multi-branch, approvals)

CREATE TABLE IF NOT EXISTS company_features (
    company_id TEXT NOT NULL,
    feature_key TEXT NOT NULL,
    is_enabled INTEGER NOT NULL DEFAULT 0,
    modified_at TEXT,
    modified_by TEXT,
    PRIMARY KEY (company_id, feature_key),
    FOREIGN KEY (company_id) REFERENCES companies(company_id)
);
