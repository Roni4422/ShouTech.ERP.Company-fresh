-- Legacy core schema snapshot moved out of Data.cs into DBA ownership.
PRAGMA foreign_keys = ON;

-- ═══════════════════════════════════════════════════════════
-- [CORE] Companies
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS companies (
    company_id TEXT NOT NULL PRIMARY KEY,
    company_code TEXT NOT NULL UNIQUE,
    company_name_ar TEXT NOT NULL,
    company_name_en TEXT NOT NULL,
    email TEXT,
    phone TEXT,
    mobile TEXT,
    address_line1 TEXT,
    commercial_registration TEXT,
    currency_code TEXT NOT NULL DEFAULT 'USD',
    language_code TEXT NOT NULL DEFAULT 'ar',
    coa_template_code TEXT NOT NULL DEFAULT 'SIMPLE',
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- ═══════════════════════════════════════════════════════════
-- [CORE] Users
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS users (
    user_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    username TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    full_name TEXT NOT NULL,
    full_name_ar TEXT NOT NULL,
    full_name_en TEXT NOT NULL,
    email TEXT,
    must_change_password INTEGER NOT NULL DEFAULT 0,
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(company_id, username),
    FOREIGN KEY(company_id) REFERENCES companies(company_id)
);

-- ═══════════════════════════════════════════════════════════
-- [CORE] Fiscal Years
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS fiscal_years (
    fiscal_year_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    company_id TEXT NOT NULL,
    fiscal_year INTEGER NOT NULL,
    database_name TEXT NOT NULL,
    start_date TEXT NOT NULL,
    end_date TEXT NOT NULL,
    is_current INTEGER NOT NULL DEFAULT 1,
    is_closed INTEGER NOT NULL DEFAULT 0,
    UNIQUE(company_id, fiscal_year),
    FOREIGN KEY(company_id) REFERENCES companies(company_id)
);

-- ═══════════════════════════════════════════════════════════
-- [CORE] Roles
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS roles (
    role_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    company_id TEXT NOT NULL,
    role_code TEXT NOT NULL,
    role_name TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    UNIQUE(company_id, role_code),
    FOREIGN KEY(company_id) REFERENCES companies(company_id)
);

-- ═══════════════════════════════════════════════════════════
-- [CORE] Permissions
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS permissions (
    permission_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    permission_code TEXT NOT NULL UNIQUE,
    permission_name TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1
);

-- ═══════════════════════════════════════════════════════════
-- [CORE] User Roles (M:N)
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS user_roles (
    user_id TEXT NOT NULL,
    role_id INTEGER NOT NULL,
    PRIMARY KEY (user_id, role_id),
    FOREIGN KEY(user_id) REFERENCES users(user_id),
    FOREIGN KEY(role_id) REFERENCES roles(role_id)
);

-- ═══════════════════════════════════════════════════════════
-- [CORE] Role Permissions (M:N)
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS role_permissions (
    role_id INTEGER NOT NULL,
    permission_id INTEGER NOT NULL,
    PRIMARY KEY (role_id, permission_id),
    FOREIGN KEY(role_id) REFERENCES roles(role_id),
    FOREIGN KEY(permission_id) REFERENCES permissions(permission_id)
);

-- ═══════════════════════════════════════════════════════════
-- [CORE] Legacy Permissions Seed (kept for backward compat)
-- ═══════════════════════════════════════════════════════════
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('COMPANY_VIEW', 'View companies');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('COMPANY_CREATE', 'Create companies');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('COMPANY_EDIT', 'Edit companies');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('COMPANY_DELETE', 'Delete companies');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('USER_VIEW', 'View users');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('USER_CREATE', 'Create users');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('USER_EDIT', 'Edit users');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('USER_DELETE', 'Delete users');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('ROLE_VIEW', 'View roles');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('ROLE_EDIT', 'Manage roles');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('PERMISSION_VIEW', 'View permissions');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('SETTINGS_EDIT', 'Manage settings');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('BACKUP_RUN', 'Run backups');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('BACKUP_RESTORE', 'Restore backups');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('ACCOUNTING_VIEW', 'View accounting');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('ACCOUNTING_EDIT', 'Manage accounting');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('JOURNAL_POST', 'Post journal entries');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('INVENTORY_VIEW', 'View inventory');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('INVENTORY_EDIT', 'Manage inventory');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('SALES_VIEW', 'View sales');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('SALES_EDIT', 'Manage sales');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('PURCHASE_VIEW', 'View purchases');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('PURCHASE_EDIT', 'Manage purchases');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('REPORT_VIEW', 'View reports');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('AUDIT_VIEW', 'View audit log');
INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('SYSTEM_VIEW', 'View system status');

-- ═══════════════════════════════════════════════════════════
-- [INV] Units of Measure
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS units_of_measure (
    uom_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    uom_code TEXT NOT NULL,
    uom_name_ar TEXT NOT NULL,
    uom_name_en TEXT,
    base_uom_id TEXT,
    conversion_factor REAL NOT NULL DEFAULT 1,
    is_base_unit INTEGER NOT NULL DEFAULT 0,
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT,
    deleted_by TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by TEXT,
    modified_at TEXT,
    modified_by TEXT,
    UNIQUE(company_id, uom_code),
    FOREIGN KEY(company_id) REFERENCES companies(company_id)
);

CREATE INDEX IF NOT EXISTS ix_uom_company
    ON units_of_measure(company_id, is_active) WHERE is_deleted = 0;

-- ═══════════════════════════════════════════════════════════
-- [INV] Product Categories (hierarchical)
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS product_categories (
    category_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    parent_category_id TEXT,
    category_code TEXT NOT NULL,
    category_name_ar TEXT NOT NULL,
    category_name_en TEXT,
    category_level INTEGER NOT NULL DEFAULT 1,
    icon_path TEXT,
    image_path TEXT,
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT,
    deleted_by TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by TEXT,
    modified_at TEXT,
    modified_by TEXT,
    UNIQUE(company_id, category_code),
    FOREIGN KEY(company_id) REFERENCES companies(company_id),
    FOREIGN KEY(parent_category_id) REFERENCES product_categories(category_id)
);

CREATE INDEX IF NOT EXISTS ix_categories_company
    ON product_categories(company_id, is_active) WHERE is_deleted = 0;

-- ═══════════════════════════════════════════════════════════
-- [INV] Brands
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS brands (
    brand_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    brand_code TEXT NOT NULL,
    brand_name_ar TEXT NOT NULL,
    brand_name_en TEXT,
    logo_path TEXT,
    website TEXT,
    description TEXT,
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT,
    deleted_by TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by TEXT,
    modified_at TEXT,
    modified_by TEXT,
    UNIQUE(company_id, brand_code),
    FOREIGN KEY(company_id) REFERENCES companies(company_id)
);

CREATE INDEX IF NOT EXISTS ix_brands_company
    ON brands(company_id, is_active) WHERE is_deleted = 0;

            -- ═══════════════════════════════════════════════════════════
-- [INV] Warehouses
-- The warehouses table is created by MatRepo.EnsureWarehouseSchema
-- immediately after this script runs. Do not redefine it here to
-- avoid schema drift between the two definitions.
-- ═══════════════════════════════════════════════════════════

-- ═══════════════════════════════════════════════════════════
-- [INV] Products (45 fields — full version)
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS products (
    product_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,

    -- Basic
    product_code TEXT NOT NULL,
    product_name_ar TEXT NOT NULL,
    product_name_en TEXT,
    short_name TEXT,
    description TEXT,

    -- Classification
    category_id TEXT,
    brand_id TEXT,

    -- UOM
    primary_uom_id TEXT NOT NULL,
    sale_uom_id TEXT,
    purchase_uom_id TEXT,

    -- Identification
    barcode TEXT,
    sku TEXT,
    quick_code TEXT,

    -- Pricing
    sale_price REAL NOT NULL DEFAULT 0,
    purchase_price REAL NOT NULL DEFAULT 0,
    cost_price REAL NOT NULL DEFAULT 0,
    min_sale_price REAL,
    max_sale_price REAL,
    last_purchase_price REAL,
    last_sale_price REAL,
    profit_margin REAL,

    -- Tax (ZATCA-ready)
    tax_category TEXT NOT NULL DEFAULT 'STANDARD'
        CHECK (tax_category IN ('STANDARD', 'ZERO', 'EXEMPT')),
    tax_rate REAL NOT NULL DEFAULT 15,
    tax_exempt_reason TEXT,
    zatca_item_code TEXT,

    -- Stock Settings
    is_stock_item INTEGER NOT NULL DEFAULT 1,
    is_service_item INTEGER NOT NULL DEFAULT 0,
    is_pos_item INTEGER NOT NULL DEFAULT 1,
    is_serialized INTEGER NOT NULL DEFAULT 0,
    is_batch_tracked INTEGER NOT NULL DEFAULT 0,
    is_expiry_tracked INTEGER NOT NULL DEFAULT 0,
    expiry_days INTEGER,
    is_composite_product INTEGER NOT NULL DEFAULT 0,

    reorder_level REAL NOT NULL DEFAULT 0,
    reorder_quantity REAL NOT NULL DEFAULT 0,
    safety_stock REAL NOT NULL DEFAULT 0,
    max_stock REAL,

    -- Dimensions
    weight REAL,
    length REAL,
    width REAL,
    height REAL,

    -- Media
    main_image_path TEXT,

    -- AI-ready
    demand_velocity INTEGER,
    product_score REAL,
    is_recommended INTEGER NOT NULL DEFAULT 0,

    -- Status
    is_active INTEGER NOT NULL DEFAULT 1,
    is_blocked INTEGER NOT NULL DEFAULT 0,
    block_reason TEXT,
    discontinued_date TEXT,

    -- Audit
    is_deleted INTEGER NOT NULL DEFAULT 0,
    deleted_at TEXT,
    deleted_by TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by TEXT,
    modified_at TEXT,
    modified_by TEXT,

    UNIQUE(company_id, product_code),
    FOREIGN KEY(company_id) REFERENCES companies(company_id),
    FOREIGN KEY(category_id) REFERENCES product_categories(category_id),
    FOREIGN KEY(brand_id) REFERENCES brands(brand_id),
    FOREIGN KEY(primary_uom_id) REFERENCES units_of_measure(uom_id),
    FOREIGN KEY(sale_uom_id) REFERENCES units_of_measure(uom_id),
    FOREIGN KEY(purchase_uom_id) REFERENCES units_of_measure(uom_id)
);

CREATE INDEX IF NOT EXISTS ix_products_company_active
    ON products(company_id, is_active) WHERE is_deleted = 0;

CREATE INDEX IF NOT EXISTS ix_products_barcode
    ON products(barcode) WHERE barcode IS NOT NULL AND is_deleted = 0;

CREATE INDEX IF NOT EXISTS ix_products_name_ar
    ON products(product_name_ar) WHERE is_deleted = 0;

CREATE INDEX IF NOT EXISTS ix_products_category
    ON products(category_id, is_active) WHERE is_deleted = 0;

CREATE INDEX IF NOT EXISTS ix_products_pos
    ON products(company_id, is_pos_item, is_active) WHERE is_deleted = 0;

-- ═══════════════════════════════════════════════════════════
-- [ACC] Journal Entries
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS journal_entries (
    journal_entry_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    entry_date TEXT NOT NULL,
    description TEXT NOT NULL,
    status INTEGER NOT NULL DEFAULT 0 CHECK (status IN (0, 1, 2)),
    created_by TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    posted_by TEXT,
    posted_at TEXT,
    reversed_by TEXT,
    reversed_at TEXT
);

-- ═══════════════════════════════════════════════════════════
-- [ACC] Journal Entry Lines
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS journal_entry_lines (
    journal_entry_line_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    journal_entry_id TEXT NOT NULL,
    line_number INTEGER NOT NULL,
    account_id TEXT NOT NULL,
    debit REAL NOT NULL DEFAULT 0,
    credit REAL NOT NULL DEFAULT 0,
    cost_center TEXT,
    note TEXT,
    UNIQUE(journal_entry_id, line_number),
    CHECK (debit >= 0 AND credit >= 0 AND ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))),
    FOREIGN KEY(journal_entry_id) REFERENCES journal_entries(journal_entry_id)
);

-- ═══════════════════════════════════════════════════════════
-- [ACC] Journal Audit Events
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS journal_audit_events (
    journal_audit_event_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    journal_entry_id TEXT NOT NULL,
    event_type INTEGER NOT NULL CHECK (event_type IN (1, 2, 3)),
    actor TEXT NOT NULL,
    event_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    details TEXT,
    FOREIGN KEY(journal_entry_id) REFERENCES journal_entries(journal_entry_id)
);

-- ═══════════════════════════════════════════════════════════
-- [INV] Stock Balances (per warehouse/product)
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS stock_balances (
    stock_balance_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    company_id TEXT NOT NULL,
    warehouse_id INTEGER NOT NULL,
    product_id TEXT NOT NULL,
    available_qty REAL NOT NULL DEFAULT 0,
    last_cost REAL,
    last_updated TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    UNIQUE (company_id, warehouse_id, product_id),
    FOREIGN KEY(company_id) REFERENCES companies(company_id),
    FOREIGN KEY(warehouse_id) REFERENCES warehouses(warehouse_id),
    FOREIGN KEY(product_id) REFERENCES products(product_id)
);

CREATE INDEX IF NOT EXISTS ix_stock_balances_product
    ON stock_balances(product_id) WHERE is_deleted = 0;

-- ═══════════════════════════════════════════════════════════
-- [INV] Stock Movements (ledger of all stock changes)
-- ═══════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS stock_movements (
    movement_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    company_id TEXT NOT NULL,
    movement_type TEXT NOT NULL,
    movement_date TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    warehouse_id_to INTEGER,
    product_id TEXT NOT NULL,
    quantity REAL NOT NULL CHECK (quantity > 0),
    unit_cost REAL,
    reference_table TEXT,
    notes TEXT,
    created_by TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(company_id) REFERENCES companies(company_id),
    FOREIGN KEY(warehouse_id_to) REFERENCES warehouses(warehouse_id),
    FOREIGN KEY(product_id) REFERENCES products(product_id)
);

CREATE INDEX IF NOT EXISTS ix_stock_movements_product
    ON stock_movements(product_id, movement_date);

CREATE INDEX IF NOT EXISTS ix_stock_movements_date
    ON stock_movements(company_id, movement_date);
