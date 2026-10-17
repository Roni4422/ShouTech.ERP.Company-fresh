-- Migration: 028_report_views_2
-- Description: More read-only ribbon report views (daily journal, dormant accounts,
-- cost-center balances, most active materials). Portable across SQLite and PostgreSQL.

DROP VIEW IF EXISTS v_rpt_daily_journal;
CREATE VIEW v_rpt_daily_journal AS
SELECT company_id,
       CAST(entry_date AS TEXT) AS entry_date,
       COUNT(DISTINCT journal_entry_id) AS entry_count,
       SUM(debit) AS debit,
       SUM(credit) AS credit
FROM v_posted_gl_lines
GROUP BY company_id, CAST(entry_date AS TEXT);

DROP VIEW IF EXISTS v_rpt_dormant_accounts;
CREATE VIEW v_rpt_dormant_accounts AS
SELECT a.company_id,
       a.account_code,
       a.account_name,
       a.account_type
FROM chart_of_accounts a
WHERE NOT EXISTS (
    SELECT 1 FROM v_posted_gl_lines g
    WHERE g.company_id = a.company_id AND g.account_id = a.account_id);

DROP VIEW IF EXISTS v_rpt_cost_center_balance;
CREATE VIEW v_rpt_cost_center_balance AS
SELECT company_id,
       COALESCE(NULLIF(cost_center, ''), '-') AS cost_center,
       SUM(debit) AS debit,
       SUM(credit) AS credit,
       SUM(debit) - SUM(credit) AS balance
FROM v_posted_gl_lines
GROUP BY company_id, COALESCE(NULLIF(cost_center, ''), '-');

DROP VIEW IF EXISTS v_rpt_active_materials;
CREATE VIEW v_rpt_active_materials AS
SELECT sm.company_id,
       COALESCE(m.code, CAST(sm.material_id AS TEXT)) AS material_code,
       COALESCE(m.name_ar, '') AS name_ar,
       COALESCE(m.name_en, '') AS name_en,
       COUNT(*) AS move_count,
       SUM(sm.qty_in) AS qty_in,
       SUM(sm.qty_out) AS qty_out
FROM material_stock_movements sm
LEFT JOIN materials m ON m.company_id = sm.company_id AND m.material_id = sm.material_id
WHERE CAST(sm.is_deleted AS INTEGER) = 0
GROUP BY sm.company_id, COALESCE(m.code, CAST(sm.material_id AS TEXT)), COALESCE(m.name_ar, ''), COALESCE(m.name_en, '');
