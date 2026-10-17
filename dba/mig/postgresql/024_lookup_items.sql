-- Migration: 024_lookup_items
-- Description: Generic company-scoped master-data lists opened from the ribbon (areas, groups, payment methods, units, ...)

CREATE TABLE IF NOT EXISTS lookup_items (
    item_id UUID NOT NULL PRIMARY KEY,
    company_id UUID NOT NULL,
    kind VARCHAR(30) NOT NULL,
    code VARCHAR(50) NOT NULL,
    name_ar VARCHAR(200) NOT NULL DEFAULT '',
    name_en VARCHAR(200) NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT true,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    created_by VARCHAR(255),
    modified_at TIMESTAMP WITH TIME ZONE,
    modified_by VARCHAR(255),
    FOREIGN KEY (company_id) REFERENCES companies(company_id),
    UNIQUE (company_id, kind, code)
);

CREATE INDEX IF NOT EXISTS ix_lookup_items_kind ON lookup_items (company_id, kind);
