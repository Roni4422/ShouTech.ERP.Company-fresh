-- Migration: 023_company_features
-- Description: Per-company enterprise feature flags (audit trail, multi-currency, multi-branch, approvals)

CREATE TABLE IF NOT EXISTS company_features (
    company_id UUID NOT NULL,
    feature_key VARCHAR(50) NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT false,
    modified_at TIMESTAMP WITH TIME ZONE,
    modified_by VARCHAR(255),
    PRIMARY KEY (company_id, feature_key),
    FOREIGN KEY (company_id) REFERENCES companies(company_id)
);
