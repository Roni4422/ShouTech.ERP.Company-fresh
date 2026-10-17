-- Migration: 025_deletion_audit_triggers (PostgreSQL)
-- Description: Explicit delete-audit triggers (soft and hard delete) for company-scoped tables.
-- Replaces runtime trigger installation previously performed by RecycleBinSvc.
-- Trigger names match the legacy runtime-installed triggers; DROP/CREATE keeps this repeatable.

CREATE OR REPLACE FUNCTION public.shoutech_audit_delete_row()
RETURNS trigger
LANGUAGE plpgsql
AS $audit$
DECLARE
    row_data jsonb;
BEGIN
    IF TG_OP = 'UPDATE' THEN
        row_data := to_jsonb(NEW);
        INSERT INTO public.deletion_audit_log
            (event_id, company_code, table_name, record_key, action_type, actor,
             occurred_at, method, reason, record_snapshot)
        VALUES
            (md5(random()::text || clock_timestamp()::text || txid_current()::text),
             COALESCE(row_data ->> TG_ARGV[1], ''), TG_TABLE_NAME,
             COALESCE(row_data ->> TG_ARGV[0], ''), 'DELETE',
             COALESCE(row_data ->> 'deleted_by', 'database-trigger'),
             clock_timestamp()::text, 'SOFT_DELETE_TRIGGER',
             'Captured by database trigger; caller did not provide a reason',
             row_data::text);
        RETURN NEW;
    END IF;
    row_data := to_jsonb(OLD);
    INSERT INTO public.deletion_audit_log
        (event_id, company_code, table_name, record_key, action_type, actor,
         occurred_at, method, reason, record_snapshot)
    VALUES
        (md5(random()::text || clock_timestamp()::text || txid_current()::text),
         COALESCE(row_data ->> TG_ARGV[1], ''), TG_TABLE_NAME,
         COALESCE(row_data ->> TG_ARGV[0], ''), 'DELETE', 'database-trigger',
         clock_timestamp()::text, 'HARD_DELETE_TRIGGER',
         'Captured by database trigger; caller did not provide a reason',
         row_data::text);
    RETURN OLD;
END;
$audit$;

-- accounts
DROP TRIGGER IF EXISTS "trg_audit_sd_accounts" ON public."accounts";
CREATE TRIGGER "trg_audit_sd_accounts"
AFTER UPDATE OF is_deleted ON public."accounts"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('account_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_accounts" ON public."accounts";
CREATE TRIGGER "trg_audit_hd_accounts"
AFTER DELETE ON public."accounts"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('account_id', 'company_id');

-- banks
DROP TRIGGER IF EXISTS "trg_audit_sd_banks" ON public."banks";
CREATE TRIGGER "trg_audit_sd_banks"
AFTER UPDATE OF is_deleted ON public."banks"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('bank_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_banks" ON public."banks";
CREATE TRIGGER "trg_audit_hd_banks"
AFTER DELETE ON public."banks"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('bank_id', 'company_id');

-- brands
DROP TRIGGER IF EXISTS "trg_audit_sd_brands" ON public."brands";
CREATE TRIGGER "trg_audit_sd_brands"
AFTER UPDATE OF is_deleted ON public."brands"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND COALESCE(NEW.is_deleted, 0) <> 0)
EXECUTE FUNCTION public.shoutech_audit_delete_row('brand_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_brands" ON public."brands";
CREATE TRIGGER "trg_audit_hd_brands"
AFTER DELETE ON public."brands"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('brand_id', 'company_id');

-- cash_vouchers
DROP TRIGGER IF EXISTS "trg_audit_hd_cash_vouchers" ON public."cash_vouchers";
CREATE TRIGGER "trg_audit_hd_cash_vouchers"
AFTER DELETE ON public."cash_vouchers"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('cash_voucher_id', 'company_id');

-- chart_of_accounts
DROP TRIGGER IF EXISTS "trg_audit_sd_chart_of_accounts" ON public."chart_of_accounts";
CREATE TRIGGER "trg_audit_sd_chart_of_accounts"
AFTER UPDATE OF is_deleted ON public."chart_of_accounts"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('account_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_chart_of_accounts" ON public."chart_of_accounts";
CREATE TRIGGER "trg_audit_hd_chart_of_accounts"
AFTER DELETE ON public."chart_of_accounts"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('account_id', 'company_id');

-- companies
DROP TRIGGER IF EXISTS "trg_audit_sd_companies" ON public."companies";
CREATE TRIGGER "trg_audit_sd_companies"
AFTER UPDATE OF is_deleted ON public."companies"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('company_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_companies" ON public."companies";
CREATE TRIGGER "trg_audit_hd_companies"
AFTER DELETE ON public."companies"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('company_id', 'company_id');

-- company_features
DROP TRIGGER IF EXISTS "trg_audit_hd_company_features" ON public."company_features";
CREATE TRIGGER "trg_audit_hd_company_features"
AFTER DELETE ON public."company_features"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('company_id', 'company_id');

-- cost_centers
DROP TRIGGER IF EXISTS "trg_audit_sd_cost_centers" ON public."cost_centers";
CREATE TRIGGER "trg_audit_sd_cost_centers"
AFTER UPDATE OF is_deleted ON public."cost_centers"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('cost_center_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_cost_centers" ON public."cost_centers";
CREATE TRIGGER "trg_audit_hd_cost_centers"
AFTER DELETE ON public."cost_centers"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('cost_center_id', 'company_id');

-- currency_rates
DROP TRIGGER IF EXISTS "trg_audit_hd_currency_rates" ON public."currency_rates";
CREATE TRIGGER "trg_audit_hd_currency_rates"
AFTER DELETE ON public."currency_rates"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('rate_id', 'company_id');

-- customers
DROP TRIGGER IF EXISTS "trg_audit_sd_customers" ON public."customers";
CREATE TRIGGER "trg_audit_sd_customers"
AFTER UPDATE OF is_deleted ON public."customers"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('customer_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_customers" ON public."customers";
CREATE TRIGGER "trg_audit_hd_customers"
AFTER DELETE ON public."customers"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('customer_id', 'company_id');

-- document_reversals
DROP TRIGGER IF EXISTS "trg_audit_hd_document_reversals" ON public."document_reversals";
CREATE TRIGGER "trg_audit_hd_document_reversals"
AFTER DELETE ON public."document_reversals"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('reversal_id', 'company_id');

-- fiscal_periods
DROP TRIGGER IF EXISTS "trg_audit_hd_fiscal_periods" ON public."fiscal_periods";
CREATE TRIGGER "trg_audit_hd_fiscal_periods"
AFTER DELETE ON public."fiscal_periods"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('fiscal_period_id', 'company_id');

-- fiscal_years
DROP TRIGGER IF EXISTS "trg_audit_hd_fiscal_years" ON public."fiscal_years";
CREATE TRIGGER "trg_audit_hd_fiscal_years"
AFTER DELETE ON public."fiscal_years"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('fiscal_year_id', 'company_id');

-- journal_entries
DROP TRIGGER IF EXISTS "trg_audit_hd_journal_entries" ON public."journal_entries";
CREATE TRIGGER "trg_audit_hd_journal_entries"
AFTER DELETE ON public."journal_entries"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('journal_entry_id', 'company_id');

-- lookup_items
DROP TRIGGER IF EXISTS "trg_audit_sd_lookup_items" ON public."lookup_items";
CREATE TRIGGER "trg_audit_sd_lookup_items"
AFTER UPDATE OF is_deleted ON public."lookup_items"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('item_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_lookup_items" ON public."lookup_items";
CREATE TRIGGER "trg_audit_hd_lookup_items"
AFTER DELETE ON public."lookup_items"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('item_id', 'company_id');

-- material_attributes
DROP TRIGGER IF EXISTS "trg_audit_hd_material_attributes" ON public."material_attributes";
CREATE TRIGGER "trg_audit_hd_material_attributes"
AFTER DELETE ON public."material_attributes"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('attr_id', 'company_id');

-- material_extra_barcodes
DROP TRIGGER IF EXISTS "trg_audit_hd_material_extra_barcodes" ON public."material_extra_barcodes";
CREATE TRIGGER "trg_audit_hd_material_extra_barcodes"
AFTER DELETE ON public."material_extra_barcodes"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('barcode_id', 'company_id');

-- material_group_sequences
DROP TRIGGER IF EXISTS "trg_audit_hd_material_group_sequences" ON public."material_group_sequences";
CREATE TRIGGER "trg_audit_hd_material_group_sequences"
AFTER DELETE ON public."material_group_sequences"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('company_id', 'company_id');

-- material_groups
DROP TRIGGER IF EXISTS "trg_audit_sd_material_groups" ON public."material_groups";
CREATE TRIGGER "trg_audit_sd_material_groups"
AFTER UPDATE OF is_deleted ON public."material_groups"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND COALESCE(NEW.is_deleted, 0) <> 0)
EXECUTE FUNCTION public.shoutech_audit_delete_row('group_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_material_groups" ON public."material_groups";
CREATE TRIGGER "trg_audit_hd_material_groups"
AFTER DELETE ON public."material_groups"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('group_id', 'company_id');

-- material_price_list
DROP TRIGGER IF EXISTS "trg_audit_hd_material_price_list" ON public."material_price_list";
CREATE TRIGGER "trg_audit_hd_material_price_list"
AFTER DELETE ON public."material_price_list"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('price_id', 'company_id');

-- material_stock_balances
DROP TRIGGER IF EXISTS "trg_audit_sd_material_stock_balances" ON public."material_stock_balances";
CREATE TRIGGER "trg_audit_sd_material_stock_balances"
AFTER UPDATE OF is_deleted ON public."material_stock_balances"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('stock_balance_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_material_stock_balances" ON public."material_stock_balances";
CREATE TRIGGER "trg_audit_hd_material_stock_balances"
AFTER DELETE ON public."material_stock_balances"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('stock_balance_id', 'company_id');

-- material_stock_movements
DROP TRIGGER IF EXISTS "trg_audit_sd_material_stock_movements" ON public."material_stock_movements";
CREATE TRIGGER "trg_audit_sd_material_stock_movements"
AFTER UPDATE OF is_deleted ON public."material_stock_movements"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('stock_movement_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_material_stock_movements" ON public."material_stock_movements";
CREATE TRIGGER "trg_audit_hd_material_stock_movements"
AFTER DELETE ON public."material_stock_movements"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('stock_movement_id', 'company_id');

-- material_suppliers
DROP TRIGGER IF EXISTS "trg_audit_hd_material_suppliers" ON public."material_suppliers";
CREATE TRIGGER "trg_audit_hd_material_suppliers"
AFTER DELETE ON public."material_suppliers"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('id', 'company_id');

-- materials
DROP TRIGGER IF EXISTS "trg_audit_sd_materials" ON public."materials";
CREATE TRIGGER "trg_audit_sd_materials"
AFTER UPDATE OF is_deleted ON public."materials"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('material_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_materials" ON public."materials";
CREATE TRIGGER "trg_audit_hd_materials"
AFTER DELETE ON public."materials"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('material_id', 'company_id');

-- outbound_messages
DROP TRIGGER IF EXISTS "trg_audit_hd_outbound_messages" ON public."outbound_messages";
CREATE TRIGGER "trg_audit_hd_outbound_messages"
AFTER DELETE ON public."outbound_messages"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('outbound_message_id', 'company_id');

-- party_ledger
DROP TRIGGER IF EXISTS "trg_audit_hd_party_ledger" ON public."party_ledger";
CREATE TRIGGER "trg_audit_hd_party_ledger"
AFTER DELETE ON public."party_ledger"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('doc_id', 'company_id');

-- product_categories
DROP TRIGGER IF EXISTS "trg_audit_sd_product_categories" ON public."product_categories";
CREATE TRIGGER "trg_audit_sd_product_categories"
AFTER UPDATE OF is_deleted ON public."product_categories"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND COALESCE(NEW.is_deleted, 0) <> 0)
EXECUTE FUNCTION public.shoutech_audit_delete_row('category_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_product_categories" ON public."product_categories";
CREATE TRIGGER "trg_audit_hd_product_categories"
AFTER DELETE ON public."product_categories"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('category_id', 'company_id');

-- products
DROP TRIGGER IF EXISTS "trg_audit_sd_products" ON public."products";
CREATE TRIGGER "trg_audit_sd_products"
AFTER UPDATE OF is_deleted ON public."products"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('product_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_products" ON public."products";
CREATE TRIGGER "trg_audit_hd_products"
AFTER DELETE ON public."products"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('product_id', 'company_id');

-- purchase_documents
DROP TRIGGER IF EXISTS "trg_audit_sd_purchase_documents" ON public."purchase_documents";
CREATE TRIGGER "trg_audit_sd_purchase_documents"
AFTER UPDATE OF is_deleted ON public."purchase_documents"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND COALESCE(NEW.is_deleted, 0) <> 0)
EXECUTE FUNCTION public.shoutech_audit_delete_row('doc_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_purchase_documents" ON public."purchase_documents";
CREATE TRIGGER "trg_audit_hd_purchase_documents"
AFTER DELETE ON public."purchase_documents"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('doc_id', 'company_id');

-- roles
DROP TRIGGER IF EXISTS "trg_audit_sd_roles" ON public."roles";
CREATE TRIGGER "trg_audit_sd_roles"
AFTER UPDATE OF is_deleted ON public."roles"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('role_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_roles" ON public."roles";
CREATE TRIGGER "trg_audit_hd_roles"
AFTER DELETE ON public."roles"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('role_id', 'company_id');

-- sales_documents
DROP TRIGGER IF EXISTS "trg_audit_sd_sales_documents" ON public."sales_documents";
CREATE TRIGGER "trg_audit_sd_sales_documents"
AFTER UPDATE OF is_deleted ON public."sales_documents"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND COALESCE(NEW.is_deleted, 0) <> 0)
EXECUTE FUNCTION public.shoutech_audit_delete_row('doc_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_sales_documents" ON public."sales_documents";
CREATE TRIGGER "trg_audit_hd_sales_documents"
AFTER DELETE ON public."sales_documents"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('doc_id', 'company_id');

-- sales_representatives
DROP TRIGGER IF EXISTS "trg_audit_sd_sales_representatives" ON public."sales_representatives";
CREATE TRIGGER "trg_audit_sd_sales_representatives"
AFTER UPDATE OF is_deleted ON public."sales_representatives"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('rep_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_sales_representatives" ON public."sales_representatives";
CREATE TRIGGER "trg_audit_hd_sales_representatives"
AFTER DELETE ON public."sales_representatives"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('rep_id', 'company_id');

-- stock_balances
DROP TRIGGER IF EXISTS "trg_audit_sd_stock_balances" ON public."stock_balances";
CREATE TRIGGER "trg_audit_sd_stock_balances"
AFTER UPDATE OF is_deleted ON public."stock_balances"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('stock_balance_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_stock_balances" ON public."stock_balances";
CREATE TRIGGER "trg_audit_hd_stock_balances"
AFTER DELETE ON public."stock_balances"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('stock_balance_id', 'company_id');

-- stock_movements
DROP TRIGGER IF EXISTS "trg_audit_hd_stock_movements" ON public."stock_movements";
CREATE TRIGGER "trg_audit_hd_stock_movements"
AFTER DELETE ON public."stock_movements"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('movement_id', 'company_id');

-- units_of_measure
DROP TRIGGER IF EXISTS "trg_audit_sd_units_of_measure" ON public."units_of_measure";
CREATE TRIGGER "trg_audit_sd_units_of_measure"
AFTER UPDATE OF is_deleted ON public."units_of_measure"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('uom_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_units_of_measure" ON public."units_of_measure";
CREATE TRIGGER "trg_audit_hd_units_of_measure"
AFTER DELETE ON public."units_of_measure"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('uom_id', 'company_id');

-- users
DROP TRIGGER IF EXISTS "trg_audit_sd_users" ON public."users";
CREATE TRIGGER "trg_audit_sd_users"
AFTER UPDATE OF is_deleted ON public."users"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND NEW.is_deleted IS TRUE)
EXECUTE FUNCTION public.shoutech_audit_delete_row('user_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_users" ON public."users";
CREATE TRIGGER "trg_audit_hd_users"
AFTER DELETE ON public."users"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('user_id', 'company_id');

-- warehouses
DROP TRIGGER IF EXISTS "trg_audit_sd_warehouses" ON public."warehouses";
CREATE TRIGGER "trg_audit_sd_warehouses"
AFTER UPDATE OF is_deleted ON public."warehouses"
FOR EACH ROW
WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted AND COALESCE(NEW.is_deleted, 0) <> 0)
EXECUTE FUNCTION public.shoutech_audit_delete_row('warehouse_id', 'company_id');

DROP TRIGGER IF EXISTS "trg_audit_hd_warehouses" ON public."warehouses";
CREATE TRIGGER "trg_audit_hd_warehouses"
AFTER DELETE ON public."warehouses"
FOR EACH ROW
EXECUTE FUNCTION public.shoutech_audit_delete_row('warehouse_id', 'company_id');
