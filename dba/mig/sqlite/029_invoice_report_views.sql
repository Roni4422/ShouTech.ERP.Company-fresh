-- Migration: 029_invoice_report_views
-- Description: Read-only views behind the Invoices ribbon reports (invoice profits,
-- quotation search, packing list). Portable across SQLite and PostgreSQL.
-- Cost is the current average cost of the line's material in the document warehouse.

DROP VIEW IF EXISTS v_rpt_invoice_profit;
CREATE VIEW v_rpt_invoice_profit AS
SELECT d.company_id,
       CAST(d.doc_date AS TEXT) AS doc_date,
       d.doc_no,
       COALESCE(d.party_name, '') AS party_name,
       SUM(COALESCE(l.line_subtotal, 0)) AS revenue,
       SUM(COALESCE(l.qty, 0) * COALESCE((
           SELECT b.average_cost FROM material_stock_balances b
           WHERE b.company_id = d.company_id AND b.material_id = l.material_id
             AND b.warehouse_id = COALESCE(l.warehouse_id, d.warehouse_id)
             AND CAST(b.is_deleted AS INTEGER) = 0), 0)) AS cost
FROM sales_documents d
JOIN sales_document_lines l ON l.doc_id = d.doc_id
WHERE d.is_deleted = 0 AND d.status = 'Posted' AND d.doc_kind IN ('SAL', 'POS')
GROUP BY d.company_id, CAST(d.doc_date AS TEXT), d.doc_no, COALESCE(d.party_name, '');

DROP VIEW IF EXISTS v_rpt_quotations;
CREATE VIEW v_rpt_quotations AS
SELECT d.company_id,
       CAST(d.doc_date AS TEXT) AS doc_date,
       d.doc_no,
       COALESCE(d.party_name, '') AS party_name,
       COALESCE(d.grand_total, 0) AS total,
       COALESCE(d.currency_code, '') AS currency,
       COALESCE(d.status, '') AS status
FROM sales_documents d
WHERE d.is_deleted = 0 AND d.doc_kind = 'QUO';

DROP VIEW IF EXISTS v_rpt_packing_list;
CREATE VIEW v_rpt_packing_list AS
SELECT d.company_id,
       CAST(d.doc_date AS TEXT) AS doc_date,
       d.doc_no,
       COALESCE(d.party_name, '') AS party_name,
       COALESCE(l.material_code, '') AS material_code,
       l.description,
       COALESCE(l.unit, '') AS unit,
       l.qty
FROM sales_documents d
JOIN sales_document_lines l ON l.doc_id = d.doc_id
WHERE d.is_deleted = 0 AND d.doc_kind IN ('SAL', 'POS');
