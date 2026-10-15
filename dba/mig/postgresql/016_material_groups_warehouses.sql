-- Material groups, card sequences, and warehouses are DBA-owned schema.
CREATE TABLE IF NOT EXISTS material_groups (
    group_id BIGSERIAL PRIMARY KEY,
    company_id TEXT NOT NULL DEFAULT '',
    card_no BIGINT,
    group_code TEXT NOT NULL,
    group_name TEXT NOT NULL DEFAULT '',
    group_name_en TEXT NOT NULL DEFAULT '',
    parent_code BIGINT,
    warehouse TEXT,
    notes TEXT NOT NULL DEFAULT '',
    general_specs TEXT NOT NULL DEFAULT '',
    image_path TEXT,
    is_sales_class INTEGER NOT NULL DEFAULT 0,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'Active',
    created_by TEXT,
    created_at TEXT,
    updated_by TEXT,
    updated_at TEXT
);
ALTER TABLE material_groups ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ;
ALTER TABLE material_groups ADD COLUMN IF NOT EXISTS deleted_by TEXT;
CREATE TABLE IF NOT EXISTS material_group_sequences (
    company_id TEXT PRIMARY KEY,
    last_card_no BIGINT NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_material_groups_company_card
    ON material_groups(company_id, card_no);
CREATE TABLE IF NOT EXISTS warehouses (
    warehouse_id BIGSERIAL PRIMARY KEY,
    company_id TEXT NOT NULL DEFAULT '',
    card_no BIGINT NOT NULL DEFAULT 0,
    warehouse_code BIGINT NOT NULL DEFAULT 0,
    warehouse_name_ar TEXT NOT NULL DEFAULT '',
    warehouse_name_en TEXT,
    parent_code BIGINT,
    address_line1 TEXT,
    city TEXT,
    region TEXT,
    country TEXT,
    phone TEXT,
    aisle TEXT,
    rack TEXT,
    shelf TEXT,
    notes TEXT,
    is_default INTEGER NOT NULL DEFAULT 0,
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    created_at TEXT,
    updated_at TEXT
);
ALTER TABLE warehouses ADD COLUMN IF NOT EXISTS deleted_at TIMESTAMPTZ;
ALTER TABLE warehouses ADD COLUMN IF NOT EXISTS deleted_by TEXT;
CREATE UNIQUE INDEX IF NOT EXISTS ux_warehouses_company_cardno
    ON warehouses(company_id, card_no) WHERE card_no > 0 AND is_deleted = 0;
CREATE INDEX IF NOT EXISTS ix_warehouses_company
    ON warehouses(company_id, is_active) WHERE is_deleted = 0;
