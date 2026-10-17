-- Migration: 027_report_views
-- Description: Read-only report views backing the generic QryWin ribbon reports
-- (negative stock, stagnant stock, cash journal, profit & loss, sales by party).
-- Portable SQL: identical on SQLite and PostgreSQL.

DROP VIEW IF EXISTS v_rpt_negative_stock;
CREATE VIEW v_rpt_negative_stock AS
SELECT s.company_id,
       CAST(COALESCE(w.warehouse_code, s.warehouse_id) AS TEXT) AS warehouse,
       COALESCE(m.code, CAST(s.material_id AS TEXT)) AS material_code,
       COALESCE(m.name_ar, '') AS name_ar,
       COALESCE(m.name_en, '') AS name_en,
       COALESCE(m.default_unit, '') AS unit,
       s.available_qty AS qty,
       s.average_cost AS avg_cost,
       s.available_qty * s.average_cost AS value
FROM material_stock_balances s
LEFT JOIN warehouses w ON w.company_id = s.company_id AND w.warehouse_id = s.warehouse_id
LEFT JOIN materials m ON m.company_id = s.company_id AND m.material_id = s.material_id
WHERE CAST(s.is_deleted AS INTEGER) = 0 AND s.available_qty < 0;

DROP VIEW IF EXISTS v_rpt_stagnant_stock;
CREATE VIEW v_rpt_stagnant_stock AS
SELECT s.company_id,
       CAST(COALESCE(w.warehouse_code, s.warehouse_id) AS TEXT) AS warehouse,
       COALESCE(m.code, CAST(s.material_id AS TEXT)) AS material_code,
       COALESCE(m.name_ar, '') AS name_ar,
       COALESCE(m.name_en, '') AS name_en,
       COALESCE(m.default_unit, '') AS unit,
       s.available_qty AS qty,
       s.average_cost AS avg_cost,
       s.available_qty * s.average_cost AS value
FROM material_stock_balances s
LEFT JOIN warehouses w ON w.company_id = s.company_id AND w.warehouse_id = s.warehouse_id
LEFT JOIN materials m ON m.company_id = s.company_id AND m.material_id = s.material_id
WHERE CAST(s.is_deleted AS INTEGER) = 0 AND s.available_qty > 0
  AND NOT EXISTS (
      SELECT 1 FROM material_stock_movements sm
      WHERE sm.company_id = s.company_id AND sm.warehouse_id = s.warehouse_id
        AND sm.material_id = s.material_id AND sm.qty_out > 0
        AND CAST(sm.is_deleted AS INTEGER) = 0);

DROP VIEW IF EXISTS v_rpt_cash_journal;
CREATE VIEW v_rpt_cash_journal AS
SELECT v.company_id,
       v.voucher_date AS doc_date,
       v.voucher_number AS doc_no,
       v.voucher_kind AS doc_kind,
       COALESCE(v.party_name, '') AS party_name,
       v.total_amount AS total,
       v.currency_code AS currency,
       v.status AS status
FROM cash_vouchers v;

DROP VIEW IF EXISTS v_rpt_profit_loss;
CREATE VIEW v_rpt_profit_loss AS
SELECT company_id,
       account_code,
       account_name,
       account_type,
       SUM(natural_amount) AS amount
FROM v_posted_gl_lines
WHERE account_type IN ('REVENUE', 'EXPENSE')
GROUP BY company_id, account_code, account_name, account_type;

DROP VIEW IF EXISTS v_rpt_sales_by_party;
CREATE VIEW v_rpt_sales_by_party AS
SELECT d.company_id,
       COALESCE(d.party_name, '') AS party_name,
       COUNT(*) AS doc_count,
       SUM(COALESCE(d.subtotal, 0)) AS subtotal,
       SUM(COALESCE(d.tax_amount, 0)) AS tax,
       SUM(COALESCE(d.grand_total, 0)) AS total
FROM sales_documents d
WHERE d.is_deleted = 0 AND d.status = 'Posted' AND d.doc_kind IN ('SAL', 'POS')
GROUP BY d.company_id, COALESCE(d.party_name, '');
