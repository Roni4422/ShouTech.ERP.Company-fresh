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

-- Year-end closing entry: same SQL as AccountingStructureRepository.PostYearEndClosingEntryAsync.
INSERT INTO chart_of_accounts(account_id, company_id, account_code, account_name, account_type) VALUES
 ('00000000-0000-0000-0000-00000000a003', '00000000-0000-0000-0000-0000000000c1', '3000', 'Retained earnings', 'EQUITY'),
 ('00000000-0000-0000-0000-00000000a004', '00000000-0000-0000-0000-0000000000c1', '5000', 'Expense', 'EXPENSE');
INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id)
VALUES ('00000000-0000-0000-0000-0000000000e3', '00000000-0000-0000-0000-0000000000c1', '2026-03-12', 'cost', 0, 'u', '00000000-0000-0000-0000-0000000000f3');
INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit) VALUES
 ('00000000-0000-0000-0000-0000000000e3', 1, '00000000-0000-0000-0000-00000000a004', 40, 0),
 ('00000000-0000-0000-0000-0000000000e3', 2, '00000000-0000-0000-0000-00000000a001', 0, 40);
UPDATE journal_entries SET status = 1 WHERE journal_entry_id = '00000000-0000-0000-0000-0000000000e3';

-- Close: revenue 100 (credit) and expense 40 (debit) -> clear both, 60 profit to retained earnings.
INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id, document_number)
SELECT '00000000-0000-0000-0000-0000000000e4', fp.company_id, fp.end_date, 'Year-end closing entry', 0, 'u', fp.fiscal_period_id, 'YE-CLOSE-1'
FROM fiscal_periods fp WHERE fp.fiscal_period_id = '00000000-0000-0000-0000-0000000000f3';
INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit, note) VALUES
 ('00000000-0000-0000-0000-0000000000e4', 1, '00000000-0000-0000-0000-00000000a002', 100, 0, 'Year-end closing'),
 ('00000000-0000-0000-0000-0000000000e4', 2, '00000000-0000-0000-0000-00000000a004', 0, 40, 'Year-end closing'),
 ('00000000-0000-0000-0000-0000000000e4', 3, '00000000-0000-0000-0000-00000000a003', 0, 60, 'Year-end closing');
UPDATE journal_entries SET status = 1, posted_by = 'u', posted_at = CURRENT_TIMESTAMP
WHERE journal_entry_id = '00000000-0000-0000-0000-0000000000e4' AND status = 0;

DO $$
DECLARE left_over integer; re_bal numeric; assets numeric; claims numeric;
BEGIN
  SELECT count(*) INTO left_over FROM (
    SELECT account_id FROM v_posted_gl_lines WHERE account_type IN ('REVENUE','EXPENSE')
    GROUP BY account_id HAVING abs(sum(debit - credit)) > 0.0001) x;
  IF left_over <> 0 THEN RAISE EXCEPTION 'P&L accounts not cleared: %', left_over; END IF;
  SELECT sum(natural_amount) INTO re_bal FROM v_posted_gl_lines WHERE account_code = '3000';
  IF re_bal <> 60 THEN RAISE EXCEPTION 'retained earnings wrong: %', re_bal; END IF;
  SELECT sum(natural_amount) INTO assets FROM v_posted_gl_lines WHERE account_type = 'ASSET';
  SELECT sum(natural_amount) INTO claims FROM v_posted_gl_lines WHERE account_type IN ('LIABILITY','EQUITY');
  IF assets <> claims THEN RAISE EXCEPTION 'balance sheet does not balance: % vs %', assets, claims; END IF;
END $$;
ROLLBACK;
SELECT 'OK: PostgreSQL financial report view verified' AS result;
