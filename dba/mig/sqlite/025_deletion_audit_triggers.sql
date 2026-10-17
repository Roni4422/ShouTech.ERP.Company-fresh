-- Migration: 025_deletion_audit_triggers
-- Description: Explicit delete-audit triggers (soft and hard delete) for company-scoped tables.
-- Replaces runtime trigger installation previously performed by RecycleBinSvc. Names match the
-- legacy runtime-installed triggers, so existing databases keep their triggers (IF NOT EXISTS).

-- accounts
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_accounts"
AFTER UPDATE OF "is_deleted" ON "accounts"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'accounts', CAST(NEW."account_id" AS TEXT),
         'DELETE', COALESCE(CAST(NEW."deleted_by" AS TEXT), 'database-trigger'), strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_object('account_id', NEW."account_id", 'company_id', NEW."company_id", 'account_code', NEW."account_code", 'parent_code', NEW."parent_code", 'name_ar', NEW."name_ar", 'name_en', NEW."name_en", 'posting_mode', NEW."posting_mode", 'status', NEW."status", 'risk', NEW."risk", 'cost_center', NEW."cost_center", 'allow_reconcile', NEW."allow_reconcile", 'taxable', NEW."taxable", 'foreign_currency', NEW."foreign_currency", 'cost_required', NEW."cost_required", 'budget', NEW."budget", 'is_default', NEW."is_default", 'tax_rate', NEW."tax_rate", 'description', NEW."description", 'balance', NEW."balance", 'debit', NEW."debit"), json_object('credit', NEW."credit", 'is_deleted', NEW."is_deleted", 'created_at', NEW."created_at", 'updated_at', NEW."updated_at", 'deleted_at', NEW."deleted_at", 'deleted_by', NEW."deleted_by")));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_accounts"
AFTER DELETE ON "accounts"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'accounts', CAST(OLD."account_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_object('account_id', OLD."account_id", 'company_id', OLD."company_id", 'account_code', OLD."account_code", 'parent_code', OLD."parent_code", 'name_ar', OLD."name_ar", 'name_en', OLD."name_en", 'posting_mode', OLD."posting_mode", 'status', OLD."status", 'risk', OLD."risk", 'cost_center', OLD."cost_center", 'allow_reconcile', OLD."allow_reconcile", 'taxable', OLD."taxable", 'foreign_currency', OLD."foreign_currency", 'cost_required', OLD."cost_required", 'budget', OLD."budget", 'is_default', OLD."is_default", 'tax_rate', OLD."tax_rate", 'description', OLD."description", 'balance', OLD."balance", 'debit', OLD."debit"), json_object('credit', OLD."credit", 'is_deleted', OLD."is_deleted", 'created_at', OLD."created_at", 'updated_at', OLD."updated_at", 'deleted_at', OLD."deleted_at", 'deleted_by', OLD."deleted_by")));
END;

-- banks
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_banks"
AFTER UPDATE OF "is_deleted" ON "banks"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'banks', CAST(NEW."bank_id" AS TEXT),
         'DELETE', COALESCE(CAST(NEW."deleted_by" AS TEXT), 'database-trigger'), strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('bank_id', NEW."bank_id", 'company_id', NEW."company_id", 'bank_code', NEW."bank_code", 'name', NEW."name", 'type', NEW."type", 'gl_account_id', NEW."gl_account_id", 'account_number', NEW."account_number", 'address', NEW."address", 'phone', NEW."phone", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'created_at', NEW."created_at", 'created_by', NEW."created_by", 'modified_at', NEW."modified_at", 'modified_by', NEW."modified_by", 'deleted_at', NEW."deleted_at", 'deleted_by', NEW."deleted_by"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_banks"
AFTER DELETE ON "banks"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'banks', CAST(OLD."bank_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('bank_id', OLD."bank_id", 'company_id', OLD."company_id", 'bank_code', OLD."bank_code", 'name', OLD."name", 'type', OLD."type", 'gl_account_id', OLD."gl_account_id", 'account_number', OLD."account_number", 'address', OLD."address", 'phone', OLD."phone", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'created_at', OLD."created_at", 'created_by', OLD."created_by", 'modified_at', OLD."modified_at", 'modified_by', OLD."modified_by", 'deleted_at', OLD."deleted_at", 'deleted_by', OLD."deleted_by"));
END;

-- brands
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_brands"
AFTER UPDATE OF "is_deleted" ON "brands"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'brands', CAST(NEW."brand_id" AS TEXT),
         'DELETE', COALESCE(CAST(NEW."deleted_by" AS TEXT), 'database-trigger'), strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('brand_id', NEW."brand_id", 'company_id', NEW."company_id", 'brand_code', NEW."brand_code", 'brand_name_ar', NEW."brand_name_ar", 'brand_name_en', NEW."brand_name_en", 'logo_path', NEW."logo_path", 'website', NEW."website", 'description', NEW."description", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'deleted_at', NEW."deleted_at", 'deleted_by', NEW."deleted_by", 'created_at', NEW."created_at", 'created_by', NEW."created_by", 'modified_at', NEW."modified_at", 'modified_by', NEW."modified_by"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_brands"
AFTER DELETE ON "brands"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'brands', CAST(OLD."brand_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('brand_id', OLD."brand_id", 'company_id', OLD."company_id", 'brand_code', OLD."brand_code", 'brand_name_ar', OLD."brand_name_ar", 'brand_name_en', OLD."brand_name_en", 'logo_path', OLD."logo_path", 'website', OLD."website", 'description', OLD."description", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'deleted_at', OLD."deleted_at", 'deleted_by', OLD."deleted_by", 'created_at', OLD."created_at", 'created_by', OLD."created_by", 'modified_at', OLD."modified_at", 'modified_by', OLD."modified_by"));
END;

-- cash_vouchers
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_cash_vouchers"
AFTER DELETE ON "cash_vouchers"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'cash_vouchers', CAST(OLD."cash_voucher_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_object('cash_voucher_id', OLD."cash_voucher_id", 'company_id', OLD."company_id", 'voucher_kind', OLD."voucher_kind", 'voucher_number', OLD."voucher_number", 'voucher_date', OLD."voucher_date", 'treasury_account_id', OLD."treasury_account_id", 'party_name', OLD."party_name", 'total_amount', OLD."total_amount", 'currency_code', OLD."currency_code", 'exchange_rate', OLD."exchange_rate", 'reference_number', OLD."reference_number", 'cost_center', OLD."cost_center", 'memo', OLD."memo", 'fiscal_period_id', OLD."fiscal_period_id", 'journal_entry_id', OLD."journal_entry_id", 'reversal_journal_entry_id', OLD."reversal_journal_entry_id", 'status', OLD."status", 'created_by', OLD."created_by", 'created_at', OLD."created_at", 'posted_by', OLD."posted_by"), json_object('posted_at', OLD."posted_at", 'reversed_by', OLD."reversed_by", 'reversed_at', OLD."reversed_at")));
END;

-- chart_of_accounts
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_chart_of_accounts"
AFTER UPDATE OF "is_deleted" ON "chart_of_accounts"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'chart_of_accounts', CAST(NEW."account_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('account_id', NEW."account_id", 'company_id', NEW."company_id", 'account_code', NEW."account_code", 'account_name', NEW."account_name", 'account_type', NEW."account_type", 'parent_account_id', NEW."parent_account_id", 'is_postable', NEW."is_postable", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'created_at', NEW."created_at"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_chart_of_accounts"
AFTER DELETE ON "chart_of_accounts"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'chart_of_accounts', CAST(OLD."account_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('account_id', OLD."account_id", 'company_id', OLD."company_id", 'account_code', OLD."account_code", 'account_name', OLD."account_name", 'account_type', OLD."account_type", 'parent_account_id', OLD."parent_account_id", 'is_postable', OLD."is_postable", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'created_at', OLD."created_at"));
END;

-- companies
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_companies"
AFTER UPDATE OF "is_deleted" ON "companies"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'companies', CAST(NEW."company_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('company_id', NEW."company_id", 'company_code', NEW."company_code", 'company_name_ar', NEW."company_name_ar", 'company_name_en', NEW."company_name_en", 'email', NEW."email", 'phone', NEW."phone", 'mobile', NEW."mobile", 'address_line1', NEW."address_line1", 'commercial_registration', NEW."commercial_registration", 'currency_code', NEW."currency_code", 'language_code', NEW."language_code", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'created_at', NEW."created_at"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_companies"
AFTER DELETE ON "companies"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'companies', CAST(OLD."company_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('company_id', OLD."company_id", 'company_code', OLD."company_code", 'company_name_ar', OLD."company_name_ar", 'company_name_en', OLD."company_name_en", 'email', OLD."email", 'phone', OLD."phone", 'mobile', OLD."mobile", 'address_line1', OLD."address_line1", 'commercial_registration', OLD."commercial_registration", 'currency_code', OLD."currency_code", 'language_code', OLD."language_code", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'created_at', OLD."created_at"));
END;

-- company_features
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_company_features"
AFTER DELETE ON "company_features"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'company_features', CAST(OLD."company_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('company_id', OLD."company_id", 'feature_key', OLD."feature_key", 'is_enabled', OLD."is_enabled", 'modified_at', OLD."modified_at", 'modified_by', OLD."modified_by"));
END;

-- cost_centers
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_cost_centers"
AFTER UPDATE OF "is_deleted" ON "cost_centers"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'cost_centers', CAST(NEW."cost_center_id" AS TEXT),
         'DELETE', COALESCE(CAST(NEW."deleted_by" AS TEXT), 'database-trigger'), strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('cost_center_id', NEW."cost_center_id", 'company_id', NEW."company_id", 'cost_center_code', NEW."cost_center_code", 'name_ar', NEW."name_ar", 'name_en', NEW."name_en", 'description', NEW."description", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'created_at', NEW."created_at", 'created_by', NEW."created_by", 'modified_at', NEW."modified_at", 'modified_by', NEW."modified_by", 'deleted_at', NEW."deleted_at", 'deleted_by', NEW."deleted_by"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_cost_centers"
AFTER DELETE ON "cost_centers"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'cost_centers', CAST(OLD."cost_center_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('cost_center_id', OLD."cost_center_id", 'company_id', OLD."company_id", 'cost_center_code', OLD."cost_center_code", 'name_ar', OLD."name_ar", 'name_en', OLD."name_en", 'description', OLD."description", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'created_at', OLD."created_at", 'created_by', OLD."created_by", 'modified_at', OLD."modified_at", 'modified_by', OLD."modified_by", 'deleted_at', OLD."deleted_at", 'deleted_by', OLD."deleted_by"));
END;

-- currency_rates
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_currency_rates"
AFTER DELETE ON "currency_rates"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'currency_rates', CAST(OLD."rate_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('rate_id', OLD."rate_id", 'company_id', OLD."company_id", 'from_currency', OLD."from_currency", 'to_currency', OLD."to_currency", 'rate', OLD."rate", 'buy_rate', OLD."buy_rate", 'sell_rate', OLD."sell_rate", 'min_rate', OLD."min_rate", 'max_rate', OLD."max_rate", 'effective_date', OLD."effective_date", 'notes', OLD."notes", 'created_by', OLD."created_by", 'created_at', OLD."created_at"));
END;

-- customers
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_customers"
AFTER UPDATE OF "is_deleted" ON "customers"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'customers', CAST(NEW."customer_id" AS TEXT),
         'DELETE', COALESCE(CAST(NEW."deleted_by" AS TEXT), 'database-trigger'), strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_object('customer_id', NEW."customer_id", 'company_id', NEW."company_id", 'customer_code', NEW."customer_code", 'name_ar', NEW."name_ar", 'name_en', NEW."name_en", 'type', NEW."type", 'tax_no', NEW."tax_no", 'cr_no', NEW."cr_no", 'credit_limit', NEW."credit_limit", 'credit_days', NEW."credit_days", 'price_tier_id', NEW."price_tier_id", 'gl_account_id', NEW."gl_account_id", 'phone', NEW."phone", 'mobile', NEW."mobile", 'email', NEW."email", 'address', NEW."address", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'created_at', NEW."created_at", 'created_by', NEW."created_by"), json_object('modified_at', NEW."modified_at", 'modified_by', NEW."modified_by", 'deleted_at', NEW."deleted_at", 'deleted_by', NEW."deleted_by")));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_customers"
AFTER DELETE ON "customers"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'customers', CAST(OLD."customer_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_object('customer_id', OLD."customer_id", 'company_id', OLD."company_id", 'customer_code', OLD."customer_code", 'name_ar', OLD."name_ar", 'name_en', OLD."name_en", 'type', OLD."type", 'tax_no', OLD."tax_no", 'cr_no', OLD."cr_no", 'credit_limit', OLD."credit_limit", 'credit_days', OLD."credit_days", 'price_tier_id', OLD."price_tier_id", 'gl_account_id', OLD."gl_account_id", 'phone', OLD."phone", 'mobile', OLD."mobile", 'email', OLD."email", 'address', OLD."address", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'created_at', OLD."created_at", 'created_by', OLD."created_by"), json_object('modified_at', OLD."modified_at", 'modified_by', OLD."modified_by", 'deleted_at', OLD."deleted_at", 'deleted_by', OLD."deleted_by")));
END;

-- document_reversals
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_document_reversals"
AFTER DELETE ON "document_reversals"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'document_reversals', CAST(OLD."reversal_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('reversal_id', OLD."reversal_id", 'company_id', OLD."company_id", 'original_doc_kind', OLD."original_doc_kind", 'original_doc_id', OLD."original_doc_id", 'reversal_doc_kind', OLD."reversal_doc_kind", 'reversal_doc_id', OLD."reversal_doc_id", 'reason', OLD."reason", 'actor', OLD."actor", 'created_at', OLD."created_at", 'status', OLD."status"));
END;

-- fiscal_periods
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_fiscal_periods"
AFTER DELETE ON "fiscal_periods"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'fiscal_periods', CAST(OLD."fiscal_period_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('fiscal_period_id', OLD."fiscal_period_id", 'company_id', OLD."company_id", 'fiscal_year_id', OLD."fiscal_year_id", 'period_number', OLD."period_number", 'period_name', OLD."period_name", 'start_date', OLD."start_date", 'end_date', OLD."end_date", 'status', OLD."status", 'closed_by', OLD."closed_by", 'closed_at', OLD."closed_at"));
END;

-- fiscal_years
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_fiscal_years"
AFTER DELETE ON "fiscal_years"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'fiscal_years', CAST(OLD."fiscal_year_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('fiscal_year_id', OLD."fiscal_year_id", 'company_id', OLD."company_id", 'fiscal_year', OLD."fiscal_year", 'database_name', OLD."database_name", 'start_date', OLD."start_date", 'end_date', OLD."end_date", 'is_current', OLD."is_current", 'is_closed', OLD."is_closed", 'closed_by', OLD."closed_by", 'closed_at', OLD."closed_at"));
END;

-- journal_entries
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_journal_entries"
AFTER DELETE ON "journal_entries"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'journal_entries', CAST(OLD."journal_entry_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('journal_entry_id', OLD."journal_entry_id", 'company_id', OLD."company_id", 'entry_date', OLD."entry_date", 'description', OLD."description", 'status', OLD."status", 'created_by', OLD."created_by", 'created_at', OLD."created_at", 'posted_by', OLD."posted_by", 'posted_at', OLD."posted_at", 'reversed_by', OLD."reversed_by", 'reversed_at', OLD."reversed_at", 'reversal_of_entry_id', OLD."reversal_of_entry_id", 'fiscal_period_id', OLD."fiscal_period_id", 'reference_number', OLD."reference_number", 'currency_code', OLD."currency_code", 'exchange_rate', OLD."exchange_rate", 'document_number', OLD."document_number"));
END;

-- lookup_items
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_lookup_items"
AFTER UPDATE OF "is_deleted" ON "lookup_items"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'lookup_items', CAST(NEW."item_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('item_id', NEW."item_id", 'company_id', NEW."company_id", 'kind', NEW."kind", 'code', NEW."code", 'name_ar', NEW."name_ar", 'name_en', NEW."name_en", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'created_at', NEW."created_at", 'created_by', NEW."created_by", 'modified_at', NEW."modified_at", 'modified_by', NEW."modified_by"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_lookup_items"
AFTER DELETE ON "lookup_items"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'lookup_items', CAST(OLD."item_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('item_id', OLD."item_id", 'company_id', OLD."company_id", 'kind', OLD."kind", 'code', OLD."code", 'name_ar', OLD."name_ar", 'name_en', OLD."name_en", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'created_at', OLD."created_at", 'created_by', OLD."created_by", 'modified_at', OLD."modified_at", 'modified_by', OLD."modified_by"));
END;

-- material_attributes
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_material_attributes"
AFTER DELETE ON "material_attributes"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'material_attributes', CAST(OLD."attr_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('attr_id', OLD."attr_id", 'company_id', OLD."company_id", 'material_id', OLD."material_id", 'attr_key', OLD."attr_key", 'attr_value', OLD."attr_value", 'attr_group', OLD."attr_group", 'sort_order', OLD."sort_order"));
END;

-- material_extra_barcodes
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_material_extra_barcodes"
AFTER DELETE ON "material_extra_barcodes"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'material_extra_barcodes', CAST(OLD."barcode_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('barcode_id', OLD."barcode_id", 'company_id', OLD."company_id", 'material_id', OLD."material_id", 'barcode', OLD."barcode", 'barcode_type', OLD."barcode_type", 'unit_name', OLD."unit_name"));
END;

-- material_group_sequences
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_material_group_sequences"
AFTER DELETE ON "material_group_sequences"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'material_group_sequences', CAST(OLD."company_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('company_id', OLD."company_id", 'last_card_no', OLD."last_card_no"));
END;

-- material_groups
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_material_groups"
AFTER UPDATE OF "is_deleted" ON "material_groups"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'material_groups', CAST(NEW."group_id" AS TEXT),
         'DELETE', COALESCE(CAST(NEW."deleted_by" AS TEXT), 'database-trigger'), strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('group_id', NEW."group_id", 'company_id', NEW."company_id", 'card_no', NEW."card_no", 'group_code', NEW."group_code", 'group_name', NEW."group_name", 'group_name_en', NEW."group_name_en", 'parent_code', NEW."parent_code", 'warehouse', NEW."warehouse", 'notes', NEW."notes", 'general_specs', NEW."general_specs", 'image_path', NEW."image_path", 'is_sales_class', NEW."is_sales_class", 'is_deleted', NEW."is_deleted", 'status', NEW."status", 'created_by', NEW."created_by", 'created_at', NEW."created_at", 'updated_by', NEW."updated_by", 'updated_at', NEW."updated_at", 'deleted_at', NEW."deleted_at", 'deleted_by', NEW."deleted_by"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_material_groups"
AFTER DELETE ON "material_groups"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'material_groups', CAST(OLD."group_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('group_id', OLD."group_id", 'company_id', OLD."company_id", 'card_no', OLD."card_no", 'group_code', OLD."group_code", 'group_name', OLD."group_name", 'group_name_en', OLD."group_name_en", 'parent_code', OLD."parent_code", 'warehouse', OLD."warehouse", 'notes', OLD."notes", 'general_specs', OLD."general_specs", 'image_path', OLD."image_path", 'is_sales_class', OLD."is_sales_class", 'is_deleted', OLD."is_deleted", 'status', OLD."status", 'created_by', OLD."created_by", 'created_at', OLD."created_at", 'updated_by', OLD."updated_by", 'updated_at', OLD."updated_at", 'deleted_at', OLD."deleted_at", 'deleted_by', OLD."deleted_by"));
END;

-- material_price_list
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_material_price_list"
AFTER DELETE ON "material_price_list"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'material_price_list', CAST(OLD."price_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('price_id', OLD."price_id", 'company_id', OLD."company_id", 'material_id', OLD."material_id", 'price_level', OLD."price_level", 'price', OLD."price", 'currency', OLD."currency", 'unit_name', OLD."unit_name", 'valid_from', OLD."valid_from", 'valid_to', OLD."valid_to"));
END;

-- material_stock_balances
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_material_stock_balances"
AFTER UPDATE OF "is_deleted" ON "material_stock_balances"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'material_stock_balances', CAST(NEW."stock_balance_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('stock_balance_id', NEW."stock_balance_id", 'company_id', NEW."company_id", 'warehouse_id', NEW."warehouse_id", 'material_id', NEW."material_id", 'available_qty', NEW."available_qty", 'reserved_qty', NEW."reserved_qty", 'average_cost', NEW."average_cost", 'last_cost', NEW."last_cost", 'last_updated', NEW."last_updated", 'is_deleted', NEW."is_deleted"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_material_stock_balances"
AFTER DELETE ON "material_stock_balances"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'material_stock_balances', CAST(OLD."stock_balance_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('stock_balance_id', OLD."stock_balance_id", 'company_id', OLD."company_id", 'warehouse_id', OLD."warehouse_id", 'material_id', OLD."material_id", 'available_qty', OLD."available_qty", 'reserved_qty', OLD."reserved_qty", 'average_cost', OLD."average_cost", 'last_cost', OLD."last_cost", 'last_updated', OLD."last_updated", 'is_deleted', OLD."is_deleted"));
END;

-- material_stock_movements
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_material_stock_movements"
AFTER UPDATE OF "is_deleted" ON "material_stock_movements"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'material_stock_movements', CAST(NEW."stock_movement_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('stock_movement_id', NEW."stock_movement_id", 'company_id', NEW."company_id", 'warehouse_id', NEW."warehouse_id", 'material_id', NEW."material_id", 'movement_date', NEW."movement_date", 'movement_type', NEW."movement_type", 'document_no', NEW."document_no", 'qty_in', NEW."qty_in", 'qty_out', NEW."qty_out", 'unit_cost', NEW."unit_cost", 'note', NEW."note", 'is_deleted', NEW."is_deleted"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_material_stock_movements"
AFTER DELETE ON "material_stock_movements"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'material_stock_movements', CAST(OLD."stock_movement_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('stock_movement_id', OLD."stock_movement_id", 'company_id', OLD."company_id", 'warehouse_id', OLD."warehouse_id", 'material_id', OLD."material_id", 'movement_date', OLD."movement_date", 'movement_type', OLD."movement_type", 'document_no', OLD."document_no", 'qty_in', OLD."qty_in", 'qty_out', OLD."qty_out", 'unit_cost', OLD."unit_cost", 'note', OLD."note", 'is_deleted', OLD."is_deleted"));
END;

-- material_suppliers
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_material_suppliers"
AFTER DELETE ON "material_suppliers"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'material_suppliers', CAST(OLD."id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('id', OLD."id", 'company_id', OLD."company_id", 'material_id', OLD."material_id", 'supplier_code', OLD."supplier_code", 'supplier_item_code', OLD."supplier_item_code", 'lead_time_days', OLD."lead_time_days", 'min_order_qty', OLD."min_order_qty", 'is_preferred', OLD."is_preferred"));
END;

-- materials
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_materials"
AFTER UPDATE OF "is_deleted" ON "materials"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'materials', CAST(NEW."material_id" AS TEXT),
         'DELETE', COALESCE(CAST(NEW."deleted_by" AS TEXT), 'database-trigger'), strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_patch(json_patch(json_patch(json_patch(json_patch(json_patch(json_patch(json_object('material_id', NEW."material_id", 'company_id', NEW."company_id", 'code', NEW."code", 'name_ar', NEW."name_ar", 'name_en', NEW."name_en", 'name_print_ar', NEW."name_print_ar", 'name_print_en', NEW."name_print_en", 'short_name', NEW."short_name", 'search_name', NEW."search_name", 'alt_code', NEW."alt_code", 'old_code', NEW."old_code", 'supplier_code', NEW."supplier_code", 'customer_code', NEW."customer_code", 'manufacturer_code', NEW."manufacturer_code", 'hs_code', NEW."hs_code", 'gtin', NEW."gtin", 'sku_external', NEW."sku_external", 'pos_name', NEW."pos_name", 'group_code', NEW."group_code", 'category', NEW."category"), json_object('sub_category', NEW."sub_category", 'brand', NEW."brand", 'model', NEW."model", 'color', NEW."color", 'size_spec', NEW."size_spec", 'grade', NEW."grade", 'class1', NEW."class1", 'class2', NEW."class2", 'class3', NEW."class3", 'class4', NEW."class4", 'class5', NEW."class5", 'product_line', NEW."product_line", 'abc_class', NEW."abc_class", 'season', NEW."season", 'item_type', NEW."item_type", 'status', NEW."status", 'lifecycle_status', NEW."lifecycle_status", 'make_or_buy', NEW."make_or_buy", 'stock_stage', NEW."stock_stage", 'is_stocked', NEW."is_stocked")), json_object('is_purchased', NEW."is_purchased", 'is_sold', NEW."is_sold", 'is_produced', NEW."is_produced", 'is_service', NEW."is_service", 'weight_net', NEW."weight_net", 'weight_gross', NEW."weight_gross", 'weight_unit', NEW."weight_unit", 'volume', NEW."volume", 'volume_unit', NEW."volume_unit", 'length', NEW."length", 'width', NEW."width", 'height', NEW."height", 'dim_unit', NEW."dim_unit", 'area', NEW."area", 'area_unit', NEW."area_unit", 'pack_qty', NEW."pack_qty", 'units_per_parcel', NEW."units_per_parcel", 'origin_country', NEW."origin_country", 'manufacturer', NEW."manufacturer", 'brand_owner', NEW."brand_owner")), json_object('source', NEW."source", 'warranty_months', NEW."warranty_months", 'catalogue_ref', NEW."catalogue_ref", 'web_url', NEW."web_url", 'tags', NEW."tags", 'unit1', NEW."unit1", 'unit1_factor', NEW."unit1_factor", 'unit1_barcode', NEW."unit1_barcode", 'unit2', NEW."unit2", 'unit2_factor', NEW."unit2_factor", 'unit2_barcode', NEW."unit2_barcode", 'unit3', NEW."unit3", 'unit3_factor', NEW."unit3_factor", 'unit3_barcode', NEW."unit3_barcode", 'unit4', NEW."unit4", 'unit4_factor', NEW."unit4_factor", 'unit4_barcode', NEW."unit4_barcode", 'unit5', NEW."unit5", 'unit5_factor', NEW."unit5_factor", 'unit5_barcode', NEW."unit5_barcode")), json_object('default_unit', NEW."default_unit", 'purchase_unit', NEW."purchase_unit", 'sales_unit', NEW."sales_unit", 'cost_method', NEW."cost_method", 'cost', NEW."cost", 'standard_cost', NEW."standard_cost", 'last_purchase_cost', NEW."last_purchase_cost", 'avg_cost', NEW."avg_cost", 'cost_currency', NEW."cost_currency", 'overhead_pct', NEW."overhead_pct", 'landed_cost', NEW."landed_cost", 'last_purchase_date', NEW."last_purchase_date", 'price_retail', NEW."price_retail", 'price_wholesale', NEW."price_wholesale", 'price_special', NEW."price_special", 'price_min', NEW."price_min", 'price_list', NEW."price_list", 'price_export', NEW."price_export", 'price_distributor', NEW."price_distributor", 'price_online', NEW."price_online")), json_object('price_currency', NEW."price_currency", 'margin_target_pct', NEW."margin_target_pct", 'discount_max_pct', NEW."discount_max_pct", 'allow_invoice_disc', NEW."allow_invoice_disc", 'price_includes_tax', NEW."price_includes_tax", 'qty_on_hand', NEW."qty_on_hand", 'min_qty', NEW."min_qty", 'max_qty', NEW."max_qty", 'reorder_qty', NEW."reorder_qty", 'safety_stock', NEW."safety_stock", 'default_warehouse', NEW."default_warehouse", 'shelf_bin', NEW."shelf_bin", 'lead_time_days', NEW."lead_time_days", 'lot_size', NEW."lot_size", 'order_multiple', NEW."order_multiple", 'economic_order_qty', NEW."economic_order_qty", 'replenishment_method', NEW."replenishment_method", 'has_expiry', NEW."has_expiry", 'shelf_life_days', NEW."shelf_life_days", 'alert_before_expiry_days', NEW."alert_before_expiry_days")), json_object('has_serial', NEW."has_serial", 'has_batch', NEW."has_batch", 'has_lot', NEW."has_lot", 'track_location', NEW."track_location", 'quality_check_required', NEW."quality_check_required", 'is_taxable', NEW."is_taxable", 'tax_rate', NEW."tax_rate", 'tax_code', NEW."tax_code", 'tax_account', NEW."tax_account", 'tax_type', NEW."tax_type", 'is_zero_rated', NEW."is_zero_rated", 'acc_inventory', NEW."acc_inventory", 'acc_sales', NEW."acc_sales", 'acc_sales_return', NEW."acc_sales_return", 'acc_cogs', NEW."acc_cogs", 'acc_purchase', NEW."acc_purchase", 'acc_purchase_return', NEW."acc_purchase_return", 'acc_expense', NEW."acc_expense", 'acc_wip', NEW."acc_wip", 'allow_sale', NEW."allow_sale")), json_object('allow_purchase', NEW."allow_purchase", 'allow_return', NEW."allow_return", 'allow_negative', NEW."allow_negative", 'commission_pct', NEW."commission_pct", 'bonus_qty', NEW."bonus_qty", 'bonus_item_code', NEW."bonus_item_code", 'min_sale_qty', NEW."min_sale_qty", 'max_sale_qty', NEW."max_sale_qty", 'sales_blocked', NEW."sales_blocked", 'image_path', NEW."image_path", 'image_path2', NEW."image_path2", 'attachment_path', NEW."attachment_path", 'description', NEW."description", 'notes', NEW."notes", 'internal_notes', NEW."internal_notes", 'spec_sheet', NEW."spec_sheet", 'label_text', NEW."label_text", 'units_json', NEW."units_json", 'prices_json', NEW."prices_json", 'attributes_json', NEW."attributes_json")), json_object('is_deleted', NEW."is_deleted", 'created_at', NEW."created_at", 'updated_at', NEW."updated_at", 'created_by', NEW."created_by", 'updated_by', NEW."updated_by", 'deleted_at', NEW."deleted_at", 'deleted_by', NEW."deleted_by")));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_materials"
AFTER DELETE ON "materials"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'materials', CAST(OLD."material_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_patch(json_patch(json_patch(json_patch(json_patch(json_patch(json_patch(json_object('material_id', OLD."material_id", 'company_id', OLD."company_id", 'code', OLD."code", 'name_ar', OLD."name_ar", 'name_en', OLD."name_en", 'name_print_ar', OLD."name_print_ar", 'name_print_en', OLD."name_print_en", 'short_name', OLD."short_name", 'search_name', OLD."search_name", 'alt_code', OLD."alt_code", 'old_code', OLD."old_code", 'supplier_code', OLD."supplier_code", 'customer_code', OLD."customer_code", 'manufacturer_code', OLD."manufacturer_code", 'hs_code', OLD."hs_code", 'gtin', OLD."gtin", 'sku_external', OLD."sku_external", 'pos_name', OLD."pos_name", 'group_code', OLD."group_code", 'category', OLD."category"), json_object('sub_category', OLD."sub_category", 'brand', OLD."brand", 'model', OLD."model", 'color', OLD."color", 'size_spec', OLD."size_spec", 'grade', OLD."grade", 'class1', OLD."class1", 'class2', OLD."class2", 'class3', OLD."class3", 'class4', OLD."class4", 'class5', OLD."class5", 'product_line', OLD."product_line", 'abc_class', OLD."abc_class", 'season', OLD."season", 'item_type', OLD."item_type", 'status', OLD."status", 'lifecycle_status', OLD."lifecycle_status", 'make_or_buy', OLD."make_or_buy", 'stock_stage', OLD."stock_stage", 'is_stocked', OLD."is_stocked")), json_object('is_purchased', OLD."is_purchased", 'is_sold', OLD."is_sold", 'is_produced', OLD."is_produced", 'is_service', OLD."is_service", 'weight_net', OLD."weight_net", 'weight_gross', OLD."weight_gross", 'weight_unit', OLD."weight_unit", 'volume', OLD."volume", 'volume_unit', OLD."volume_unit", 'length', OLD."length", 'width', OLD."width", 'height', OLD."height", 'dim_unit', OLD."dim_unit", 'area', OLD."area", 'area_unit', OLD."area_unit", 'pack_qty', OLD."pack_qty", 'units_per_parcel', OLD."units_per_parcel", 'origin_country', OLD."origin_country", 'manufacturer', OLD."manufacturer", 'brand_owner', OLD."brand_owner")), json_object('source', OLD."source", 'warranty_months', OLD."warranty_months", 'catalogue_ref', OLD."catalogue_ref", 'web_url', OLD."web_url", 'tags', OLD."tags", 'unit1', OLD."unit1", 'unit1_factor', OLD."unit1_factor", 'unit1_barcode', OLD."unit1_barcode", 'unit2', OLD."unit2", 'unit2_factor', OLD."unit2_factor", 'unit2_barcode', OLD."unit2_barcode", 'unit3', OLD."unit3", 'unit3_factor', OLD."unit3_factor", 'unit3_barcode', OLD."unit3_barcode", 'unit4', OLD."unit4", 'unit4_factor', OLD."unit4_factor", 'unit4_barcode', OLD."unit4_barcode", 'unit5', OLD."unit5", 'unit5_factor', OLD."unit5_factor", 'unit5_barcode', OLD."unit5_barcode")), json_object('default_unit', OLD."default_unit", 'purchase_unit', OLD."purchase_unit", 'sales_unit', OLD."sales_unit", 'cost_method', OLD."cost_method", 'cost', OLD."cost", 'standard_cost', OLD."standard_cost", 'last_purchase_cost', OLD."last_purchase_cost", 'avg_cost', OLD."avg_cost", 'cost_currency', OLD."cost_currency", 'overhead_pct', OLD."overhead_pct", 'landed_cost', OLD."landed_cost", 'last_purchase_date', OLD."last_purchase_date", 'price_retail', OLD."price_retail", 'price_wholesale', OLD."price_wholesale", 'price_special', OLD."price_special", 'price_min', OLD."price_min", 'price_list', OLD."price_list", 'price_export', OLD."price_export", 'price_distributor', OLD."price_distributor", 'price_online', OLD."price_online")), json_object('price_currency', OLD."price_currency", 'margin_target_pct', OLD."margin_target_pct", 'discount_max_pct', OLD."discount_max_pct", 'allow_invoice_disc', OLD."allow_invoice_disc", 'price_includes_tax', OLD."price_includes_tax", 'qty_on_hand', OLD."qty_on_hand", 'min_qty', OLD."min_qty", 'max_qty', OLD."max_qty", 'reorder_qty', OLD."reorder_qty", 'safety_stock', OLD."safety_stock", 'default_warehouse', OLD."default_warehouse", 'shelf_bin', OLD."shelf_bin", 'lead_time_days', OLD."lead_time_days", 'lot_size', OLD."lot_size", 'order_multiple', OLD."order_multiple", 'economic_order_qty', OLD."economic_order_qty", 'replenishment_method', OLD."replenishment_method", 'has_expiry', OLD."has_expiry", 'shelf_life_days', OLD."shelf_life_days", 'alert_before_expiry_days', OLD."alert_before_expiry_days")), json_object('has_serial', OLD."has_serial", 'has_batch', OLD."has_batch", 'has_lot', OLD."has_lot", 'track_location', OLD."track_location", 'quality_check_required', OLD."quality_check_required", 'is_taxable', OLD."is_taxable", 'tax_rate', OLD."tax_rate", 'tax_code', OLD."tax_code", 'tax_account', OLD."tax_account", 'tax_type', OLD."tax_type", 'is_zero_rated', OLD."is_zero_rated", 'acc_inventory', OLD."acc_inventory", 'acc_sales', OLD."acc_sales", 'acc_sales_return', OLD."acc_sales_return", 'acc_cogs', OLD."acc_cogs", 'acc_purchase', OLD."acc_purchase", 'acc_purchase_return', OLD."acc_purchase_return", 'acc_expense', OLD."acc_expense", 'acc_wip', OLD."acc_wip", 'allow_sale', OLD."allow_sale")), json_object('allow_purchase', OLD."allow_purchase", 'allow_return', OLD."allow_return", 'allow_negative', OLD."allow_negative", 'commission_pct', OLD."commission_pct", 'bonus_qty', OLD."bonus_qty", 'bonus_item_code', OLD."bonus_item_code", 'min_sale_qty', OLD."min_sale_qty", 'max_sale_qty', OLD."max_sale_qty", 'sales_blocked', OLD."sales_blocked", 'image_path', OLD."image_path", 'image_path2', OLD."image_path2", 'attachment_path', OLD."attachment_path", 'description', OLD."description", 'notes', OLD."notes", 'internal_notes', OLD."internal_notes", 'spec_sheet', OLD."spec_sheet", 'label_text', OLD."label_text", 'units_json', OLD."units_json", 'prices_json', OLD."prices_json", 'attributes_json', OLD."attributes_json")), json_object('is_deleted', OLD."is_deleted", 'created_at', OLD."created_at", 'updated_at', OLD."updated_at", 'created_by', OLD."created_by", 'updated_by', OLD."updated_by", 'deleted_at', OLD."deleted_at", 'deleted_by', OLD."deleted_by")));
END;

-- outbound_messages
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_outbound_messages"
AFTER DELETE ON "outbound_messages"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'outbound_messages', CAST(OLD."outbound_message_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('outbound_message_id', OLD."outbound_message_id", 'company_id', OLD."company_id", 'channel', OLD."channel", 'recipient_address', OLD."recipient_address", 'subject', OLD."subject", 'body', OLD."body", 'body_format', OLD."body_format", 'status', OLD."status", 'attempt_count', OLD."attempt_count", 'max_attempts', OLD."max_attempts", 'next_attempt_at', OLD."next_attempt_at", 'last_error', OLD."last_error", 'created_at', OLD."created_at", 'updated_at', OLD."updated_at", 'sent_at', OLD."sent_at"));
END;

-- party_ledger
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_party_ledger"
AFTER DELETE ON "party_ledger"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'party_ledger', CAST(OLD."doc_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('ledger_id', OLD."ledger_id", 'company_id', OLD."company_id", 'party_kind', OLD."party_kind", 'party_id', OLD."party_id", 'doc_kind', OLD."doc_kind", 'doc_id', OLD."doc_id", 'doc_no', OLD."doc_no", 'entry_date', OLD."entry_date", 'debit', OLD."debit", 'credit', OLD."credit", 'currency_code', OLD."currency_code", 'note', OLD."note", 'created_at', OLD."created_at"));
END;

-- product_categories
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_product_categories"
AFTER UPDATE OF "is_deleted" ON "product_categories"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'product_categories', CAST(NEW."category_id" AS TEXT),
         'DELETE', COALESCE(CAST(NEW."deleted_by" AS TEXT), 'database-trigger'), strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('category_id', NEW."category_id", 'company_id', NEW."company_id", 'parent_category_id', NEW."parent_category_id", 'category_code', NEW."category_code", 'category_name_ar', NEW."category_name_ar", 'category_name_en', NEW."category_name_en", 'category_level', NEW."category_level", 'icon_path', NEW."icon_path", 'image_path', NEW."image_path", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'deleted_at', NEW."deleted_at", 'deleted_by', NEW."deleted_by", 'created_at', NEW."created_at", 'created_by', NEW."created_by", 'modified_at', NEW."modified_at", 'modified_by', NEW."modified_by"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_product_categories"
AFTER DELETE ON "product_categories"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'product_categories', CAST(OLD."category_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('category_id', OLD."category_id", 'company_id', OLD."company_id", 'parent_category_id', OLD."parent_category_id", 'category_code', OLD."category_code", 'category_name_ar', OLD."category_name_ar", 'category_name_en', OLD."category_name_en", 'category_level', OLD."category_level", 'icon_path', OLD."icon_path", 'image_path', OLD."image_path", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'deleted_at', OLD."deleted_at", 'deleted_by', OLD."deleted_by", 'created_at', OLD."created_at", 'created_by', OLD."created_by", 'modified_at', OLD."modified_at", 'modified_by', OLD."modified_by"));
END;

-- products
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_products"
AFTER UPDATE OF "is_deleted" ON "products"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'products', CAST(NEW."product_id" AS TEXT),
         'DELETE', COALESCE(CAST(NEW."deleted_by" AS TEXT), 'database-trigger'), strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('product_id', NEW."product_id", 'company_id', NEW."company_id", 'product_code', NEW."product_code", 'product_name_ar', NEW."product_name_ar", 'product_name_en', NEW."product_name_en", 'category_id', NEW."category_id", 'primary_uom_id', NEW."primary_uom_id", 'default_sale_price', NEW."default_sale_price", 'default_purchase_price', NEW."default_purchase_price", 'cost_price', NEW."cost_price", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'created_by', NEW."created_by", 'created_at', NEW."created_at", 'updated_by', NEW."updated_by", 'updated_at', NEW."updated_at", 'deleted_by', NEW."deleted_by", 'deleted_at', NEW."deleted_at"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_products"
AFTER DELETE ON "products"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'products', CAST(OLD."product_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('product_id', OLD."product_id", 'company_id', OLD."company_id", 'product_code', OLD."product_code", 'product_name_ar', OLD."product_name_ar", 'product_name_en', OLD."product_name_en", 'category_id', OLD."category_id", 'primary_uom_id', OLD."primary_uom_id", 'default_sale_price', OLD."default_sale_price", 'default_purchase_price', OLD."default_purchase_price", 'cost_price', OLD."cost_price", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'created_by', OLD."created_by", 'created_at', OLD."created_at", 'updated_by', OLD."updated_by", 'updated_at', OLD."updated_at", 'deleted_by', OLD."deleted_by", 'deleted_at', OLD."deleted_at"));
END;

-- purchase_documents
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_purchase_documents"
AFTER UPDATE OF "is_deleted" ON "purchase_documents"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'purchase_documents', CAST(NEW."doc_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_object('doc_id', NEW."doc_id", 'company_id', NEW."company_id", 'doc_kind', NEW."doc_kind", 'doc_no', NEW."doc_no", 'doc_date', NEW."doc_date", 'party_id', NEW."party_id", 'party_name', NEW."party_name", 'warehouse_id', NEW."warehouse_id", 'currency_code', NEW."currency_code", 'exchange_rate', NEW."exchange_rate", 'subtotal', NEW."subtotal", 'discount_amount', NEW."discount_amount", 'tax_amount', NEW."tax_amount", 'grand_total', NEW."grand_total", 'paid_amount', NEW."paid_amount", 'status', NEW."status", 'tax_code', NEW."tax_code", 'tax_rate', NEW."tax_rate", 'notes', NEW."notes", 'created_by', NEW."created_by"), json_object('created_at', NEW."created_at", 'posted_by', NEW."posted_by", 'posted_at', NEW."posted_at", 'is_deleted', NEW."is_deleted")));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_purchase_documents"
AFTER DELETE ON "purchase_documents"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'purchase_documents', CAST(OLD."doc_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_object('doc_id', OLD."doc_id", 'company_id', OLD."company_id", 'doc_kind', OLD."doc_kind", 'doc_no', OLD."doc_no", 'doc_date', OLD."doc_date", 'party_id', OLD."party_id", 'party_name', OLD."party_name", 'warehouse_id', OLD."warehouse_id", 'currency_code', OLD."currency_code", 'exchange_rate', OLD."exchange_rate", 'subtotal', OLD."subtotal", 'discount_amount', OLD."discount_amount", 'tax_amount', OLD."tax_amount", 'grand_total', OLD."grand_total", 'paid_amount', OLD."paid_amount", 'status', OLD."status", 'tax_code', OLD."tax_code", 'tax_rate', OLD."tax_rate", 'notes', OLD."notes", 'created_by', OLD."created_by"), json_object('created_at', OLD."created_at", 'posted_by', OLD."posted_by", 'posted_at', OLD."posted_at", 'is_deleted', OLD."is_deleted")));
END;

-- roles
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_roles"
AFTER UPDATE OF "is_deleted" ON "roles"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'roles', CAST(NEW."role_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('role_id', NEW."role_id", 'company_id', NEW."company_id", 'role_code', NEW."role_code", 'role_name', NEW."role_name", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_roles"
AFTER DELETE ON "roles"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'roles', CAST(OLD."role_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('role_id', OLD."role_id", 'company_id', OLD."company_id", 'role_code', OLD."role_code", 'role_name', OLD."role_name", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted"));
END;

-- sales_documents
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_sales_documents"
AFTER UPDATE OF "is_deleted" ON "sales_documents"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'sales_documents', CAST(NEW."doc_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_object('doc_id', NEW."doc_id", 'company_id', NEW."company_id", 'doc_kind', NEW."doc_kind", 'doc_no', NEW."doc_no", 'doc_date', NEW."doc_date", 'party_id', NEW."party_id", 'party_name', NEW."party_name", 'warehouse_id', NEW."warehouse_id", 'currency_code', NEW."currency_code", 'exchange_rate', NEW."exchange_rate", 'subtotal', NEW."subtotal", 'discount_amount', NEW."discount_amount", 'tax_amount', NEW."tax_amount", 'grand_total', NEW."grand_total", 'paid_amount', NEW."paid_amount", 'status', NEW."status", 'tax_code', NEW."tax_code", 'tax_rate', NEW."tax_rate", 'notes', NEW."notes", 'created_by', NEW."created_by"), json_object('created_at', NEW."created_at", 'posted_by', NEW."posted_by", 'posted_at', NEW."posted_at", 'is_deleted', NEW."is_deleted")));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_sales_documents"
AFTER DELETE ON "sales_documents"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'sales_documents', CAST(OLD."doc_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_object('doc_id', OLD."doc_id", 'company_id', OLD."company_id", 'doc_kind', OLD."doc_kind", 'doc_no', OLD."doc_no", 'doc_date', OLD."doc_date", 'party_id', OLD."party_id", 'party_name', OLD."party_name", 'warehouse_id', OLD."warehouse_id", 'currency_code', OLD."currency_code", 'exchange_rate', OLD."exchange_rate", 'subtotal', OLD."subtotal", 'discount_amount', OLD."discount_amount", 'tax_amount', OLD."tax_amount", 'grand_total', OLD."grand_total", 'paid_amount', OLD."paid_amount", 'status', OLD."status", 'tax_code', OLD."tax_code", 'tax_rate', OLD."tax_rate", 'notes', OLD."notes", 'created_by', OLD."created_by"), json_object('created_at', OLD."created_at", 'posted_by', OLD."posted_by", 'posted_at', OLD."posted_at", 'is_deleted', OLD."is_deleted")));
END;

-- sales_representatives
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_sales_representatives"
AFTER UPDATE OF "is_deleted" ON "sales_representatives"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'sales_representatives', CAST(NEW."rep_id" AS TEXT),
         'DELETE', COALESCE(CAST(NEW."deleted_by" AS TEXT), 'database-trigger'), strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('rep_id', NEW."rep_id", 'company_id', NEW."company_id", 'rep_code', NEW."rep_code", 'name', NEW."name", 'area', NEW."area", 'commission_rate', NEW."commission_rate", 'phone', NEW."phone", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'created_at', NEW."created_at", 'created_by', NEW."created_by", 'modified_at', NEW."modified_at", 'modified_by', NEW."modified_by", 'deleted_at', NEW."deleted_at", 'deleted_by', NEW."deleted_by"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_sales_representatives"
AFTER DELETE ON "sales_representatives"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'sales_representatives', CAST(OLD."rep_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('rep_id', OLD."rep_id", 'company_id', OLD."company_id", 'rep_code', OLD."rep_code", 'name', OLD."name", 'area', OLD."area", 'commission_rate', OLD."commission_rate", 'phone', OLD."phone", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'created_at', OLD."created_at", 'created_by', OLD."created_by", 'modified_at', OLD."modified_at", 'modified_by', OLD."modified_by", 'deleted_at', OLD."deleted_at", 'deleted_by', OLD."deleted_by"));
END;

-- stock_balances
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_stock_balances"
AFTER UPDATE OF "is_deleted" ON "stock_balances"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'stock_balances', CAST(NEW."stock_balance_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('stock_balance_id', NEW."stock_balance_id", 'company_id', NEW."company_id", 'warehouse_id', NEW."warehouse_id", 'product_id', NEW."product_id", 'available_qty', NEW."available_qty", 'last_cost', NEW."last_cost", 'last_updated', NEW."last_updated", 'is_deleted', NEW."is_deleted"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_stock_balances"
AFTER DELETE ON "stock_balances"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'stock_balances', CAST(OLD."stock_balance_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('stock_balance_id', OLD."stock_balance_id", 'company_id', OLD."company_id", 'warehouse_id', OLD."warehouse_id", 'product_id', OLD."product_id", 'available_qty', OLD."available_qty", 'last_cost', OLD."last_cost", 'last_updated', OLD."last_updated", 'is_deleted', OLD."is_deleted"));
END;

-- stock_movements
CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_stock_movements"
AFTER DELETE ON "stock_movements"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'stock_movements', CAST(OLD."movement_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('movement_id', OLD."movement_id", 'company_id', OLD."company_id", 'movement_type', OLD."movement_type", 'movement_date', OLD."movement_date", 'warehouse_id_to', OLD."warehouse_id_to", 'product_id', OLD."product_id", 'quantity', OLD."quantity", 'unit_cost', OLD."unit_cost", 'reference_table', OLD."reference_table", 'notes', OLD."notes", 'created_by', OLD."created_by", 'created_at', OLD."created_at"));
END;

-- units_of_measure
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_units_of_measure"
AFTER UPDATE OF "is_deleted" ON "units_of_measure"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'units_of_measure', CAST(NEW."uom_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('uom_id', NEW."uom_id", 'company_id', NEW."company_id", 'uom_code', NEW."uom_code", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_units_of_measure"
AFTER DELETE ON "units_of_measure"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'units_of_measure', CAST(OLD."uom_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('uom_id', OLD."uom_id", 'company_id', OLD."company_id", 'uom_code', OLD."uom_code", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted"));
END;

-- users
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_users"
AFTER UPDATE OF "is_deleted" ON "users"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'users', CAST(NEW."user_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('user_id', NEW."user_id", 'company_id', NEW."company_id", 'username', NEW."username", 'password_hash', NEW."password_hash", 'password_salt', NEW."password_salt", 'full_name', NEW."full_name", 'full_name_ar', NEW."full_name_ar", 'full_name_en', NEW."full_name_en", 'email', NEW."email", 'must_change_password', NEW."must_change_password", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'created_at', NEW."created_at"));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_users"
AFTER DELETE ON "users"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'users', CAST(OLD."user_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_object('user_id', OLD."user_id", 'company_id', OLD."company_id", 'username', OLD."username", 'password_hash', OLD."password_hash", 'password_salt', OLD."password_salt", 'full_name', OLD."full_name", 'full_name_ar', OLD."full_name_ar", 'full_name_en', OLD."full_name_en", 'email', OLD."email", 'must_change_password', OLD."must_change_password", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'created_at', OLD."created_at"));
END;

-- warehouses
CREATE TRIGGER IF NOT EXISTS "trg_audit_soft_delete_warehouses"
AFTER UPDATE OF "is_deleted" ON "warehouses"
WHEN COALESCE(OLD."is_deleted", 0) = 0 AND COALESCE(NEW."is_deleted", 0) <> 0
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(NEW."company_id" AS TEXT), 'warehouses', CAST(NEW."warehouse_id" AS TEXT),
         'DELETE', COALESCE(CAST(NEW."deleted_by" AS TEXT), 'database-trigger'), strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'SOFT_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_object('warehouse_id', NEW."warehouse_id", 'company_id', NEW."company_id", 'card_no', NEW."card_no", 'warehouse_code', NEW."warehouse_code", 'warehouse_name_ar', NEW."warehouse_name_ar", 'warehouse_name_en', NEW."warehouse_name_en", 'parent_code', NEW."parent_code", 'address_line1', NEW."address_line1", 'city', NEW."city", 'region', NEW."region", 'country', NEW."country", 'phone', NEW."phone", 'aisle', NEW."aisle", 'rack', NEW."rack", 'shelf', NEW."shelf", 'notes', NEW."notes", 'is_default', NEW."is_default", 'is_active', NEW."is_active", 'is_deleted', NEW."is_deleted", 'created_at', NEW."created_at"), json_object('updated_at', NEW."updated_at", 'deleted_at', NEW."deleted_at", 'deleted_by', NEW."deleted_by")));
END;

CREATE TRIGGER IF NOT EXISTS "trg_audit_hard_delete_warehouses"
AFTER DELETE ON "warehouses"
BEGIN
    INSERT INTO deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor, occurred_at, method, reason, record_snapshot)
    VALUES
        (lower(hex(randomblob(16))), CAST(OLD."company_id" AS TEXT), 'warehouses', CAST(OLD."warehouse_id" AS TEXT),
         'DELETE', 'database-trigger', strftime('%Y-%m-%dT%H:%M:%fZ','now'), 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         json_patch(json_object('warehouse_id', OLD."warehouse_id", 'company_id', OLD."company_id", 'card_no', OLD."card_no", 'warehouse_code', OLD."warehouse_code", 'warehouse_name_ar', OLD."warehouse_name_ar", 'warehouse_name_en', OLD."warehouse_name_en", 'parent_code', OLD."parent_code", 'address_line1', OLD."address_line1", 'city', OLD."city", 'region', OLD."region", 'country', OLD."country", 'phone', OLD."phone", 'aisle', OLD."aisle", 'rack', OLD."rack", 'shelf', OLD."shelf", 'notes', OLD."notes", 'is_default', OLD."is_default", 'is_active', OLD."is_active", 'is_deleted', OLD."is_deleted", 'created_at', OLD."created_at"), json_object('updated_at', OLD."updated_at", 'deleted_at', OLD."deleted_at", 'deleted_by', OLD."deleted_by")));
END;
