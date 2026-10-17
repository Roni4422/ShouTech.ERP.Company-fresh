-- Run against a database where every dba/mig/postgresql migration has been applied:
--   psql -v ON_ERROR_STOP=1 -f tests/sql/test_financial_views_pg.sql
BEGIN;
INSERT INTO companies(company_id, company_code, company_name_ar, company_name_en)
VALUES ('00000000-0000-0000-0000-0000000000c1', 'T1', 'ش', 'Test Co');
INSERT INTO chart_of_accounts(account_id, company_id, account_code, account_name, account_type) VALUES
 ('00000000-0000-0000-0000-00000000a001', '00000000-0000-0000-0000-0000000000c1', '1000', 'Cash', 'ASSET'),
 ('00000000-0000-0000-0000-00000000a002', '00000000-0000-0000-0000-0000000000c1', '4000', 'Revenue', 'REVENUE');
INSERT INTO fiscal_years(company_id, fiscal_year, database_name, start_date, end_date)
VALUES ('00000000-0000-0000-0000-0000000000c1', 2026, '', '2026-01-01', '2026-12-31');
INSERT INTO fiscal_periods(fiscal_period_id, company_id, fiscal_year_id, period_number, period_name, start_date, end_date)
SELECT '00000000-0000-0000-0000-0000000000f3', company_id, fiscal_year_id, 3, '2026-03', '2026-03-01', '2026-03-31'
FROM fiscal_years WHERE company_id = '00000000-0000-0000-0000-0000000000c1';

INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id)
VALUES ('00000000-0000-0000-0000-0000000000e1', '00000000-0000-0000-0000-0000000000c1', '2026-03-10', 'sale', 0, 'u', '00000000-0000-0000-0000-0000000000f3'),
       ('00000000-0000-0000-0000-0000000000e2', '00000000-0000-0000-0000-0000000000c1', '2026-03-11', 'draft', 0, 'u', '00000000-0000-0000-0000-0000000000f3');
INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit) VALUES
 ('00000000-0000-0000-0000-0000000000e1', 1, '00000000-0000-0000-0000-00000000a001', 100, 0),
 ('00000000-0000-0000-0000-0000000000e1', 2, '00000000-0000-0000-0000-00000000a002', 0, 100),
 ('00000000-0000-0000-0000-0000000000e2', 1, '00000000-0000-0000-0000-00000000a001', 999, 0),
 ('00000000-0000-0000-0000-0000000000e2', 2, '00000000-0000-0000-0000-00000000a002', 0, 999);
UPDATE journal_entries SET status = 1 WHERE journal_entry_id = '00000000-0000-0000-0000-0000000000e1';

DO $$
DECLARE dr numeric; cr numeric; rev numeric;
BEGIN
  SELECT sum(debit), sum(credit) INTO dr, cr FROM v_posted_gl_lines;
  IF dr <> 100 OR cr <> 100 THEN RAISE EXCEPTION 'posted totals wrong: % / %', dr, cr; END IF;
  SELECT sum(natural_amount) INTO rev FROM v_posted_gl_lines WHERE account_code = '4000';
  IF rev <> 100 THEN RAISE EXCEPTION 'revenue natural amount wrong: %', rev; END IF;
END $$;
ROLLBACK;
SELECT 'OK: PostgreSQL financial report view verified' AS result;
