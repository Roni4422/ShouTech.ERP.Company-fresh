-- Migration: 024_lookup_items
-- Description: Generic company-scoped master-data lists opened from the ribbon (areas, groups, payment methods, units, ...)

CREATE TABLE IF NOT EXISTS lookup_items (
    item_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    code TEXT NOT NULL,
    name_ar TEXT NOT NULL DEFAULT '',
    name_en TEXT NOT NULL DEFAULT '',
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    created_by TEXT,
    modified_at TEXT,
    modified_by TEXT,
    FOREIGN KEY (company_id) REFERENCES companies(company_id),
    UNIQUE (company_id, kind, code)
);

CREATE INDEX IF NOT EXISTS ix_lookup_items_kind ON lookup_items (company_id, kind);
