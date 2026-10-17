-- Run against a database where every dba/mig/postgresql migration has been applied:
--   psql -v ON_ERROR_STOP=1 -f tests/sql/test_journal_integrity_pg.sql
-- Verifies migration 026 invariants. Rolls back, leaving no data.
BEGIN;
INSERT INTO companies(company_id, company_code, company_name_ar, company_name_en)
VALUES ('00000000-0000-0000-0000-0000000000c9', 'JI', 'ش', 'JI Co');
INSERT INTO chart_of_accounts(account_id, company_id, account_code, account_name, account_type) VALUES
 ('00000000-0000-0000-0000-0000000009a1', '00000000-0000-0000-0000-0000000000c9', '1000', 'Cash', 'ASSET'),
 ('00000000-0000-0000-0000-0000000009a2', '00000000-0000-0000-0000-0000000000c9', '4000', 'Revenue', 'REVENUE');
INSERT INTO fiscal_years(company_id, fiscal_year, database_name, start_date, end_date)
VALUES ('00000000-0000-0000-0000-0000000000c9', 2026, '', '2026-01-01', '2026-12-31');
INSERT INTO fiscal_periods(fiscal_period_id, company_id, fiscal_year_id, period_number, period_name, start_date, end_date)
SELECT '00000000-0000-0000-0000-0000000009f3', company_id, fiscal_year_id, 3, '2026-03', '2026-03-01', '2026-03-31'
FROM fiscal_years WHERE company_id = '00000000-0000-0000-0000-0000000000c9';
INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id) VALUES
 ('00000000-0000-0000-0000-0000000009e1', '00000000-0000-0000-0000-0000000000c9', '2026-03-10', 'empty', 0, 'u', '00000000-0000-0000-0000-0000000009f3'),
 ('00000000-0000-0000-0000-0000000009e2', '00000000-0000-0000-0000-0000000000c9', '2026-03-10', 'unbalanced', 0, 'u', '00000000-0000-0000-0000-0000000009f3'),
 ('00000000-0000-0000-0000-0000000009e3', '00000000-0000-0000-0000-0000000000c9', '2026-03-10', 'good', 0, 'u', '00000000-0000-0000-0000-0000000009f3');
INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit) VALUES
 ('00000000-0000-0000-0000-0000000009e2', 1, '00000000-0000-0000-0000-0000000009a1', 100, 0),
 ('00000000-0000-0000-0000-0000000009e2', 2, '00000000-0000-0000-0000-0000000009a2', 0, 90),
 ('00000000-0000-0000-0000-0000000009e3', 1, '00000000-0000-0000-0000-0000000009a1', 100, 0),
 ('00000000-0000-0000-0000-0000000009e3', 2, '00000000-0000-0000-0000-0000000009a2', 0, 100);

CREATE FUNCTION pg_temp.expect_fail(sql text, label text) RETURNS void AS $$
BEGIN
  BEGIN
    EXECUTE sql;
  EXCEPTION WHEN OTHERS THEN
    RETURN;
  END;
  RAISE EXCEPTION 'expected failure did not occur: %', label;
END;
$$ LANGUAGE plpgsql;

SELECT pg_temp.expect_fail($q$UPDATE journal_entries SET status = 1 WHERE journal_entry_id = '00000000-0000-0000-0000-0000000009e1'$q$, 'post empty');
SELECT pg_temp.expect_fail($q$UPDATE journal_entries SET status = 1 WHERE journal_entry_id = '00000000-0000-0000-0000-0000000009e2'$q$, 'post unbalanced');
UPDATE journal_entries SET status = 1 WHERE journal_entry_id = '00000000-0000-0000-0000-0000000009e3';
SELECT pg_temp.expect_fail($q$UPDATE journal_entry_lines SET debit = 5 WHERE journal_entry_id = '00000000-0000-0000-0000-0000000009e3' AND line_number = 1$q$, 'edit posted line');
SELECT pg_temp.expect_fail($q$DELETE FROM journal_entry_lines WHERE journal_entry_id = '00000000-0000-0000-0000-0000000009e3'$q$, 'delete posted line');
SELECT pg_temp.expect_fail($q$INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit) VALUES ('00000000-0000-0000-0000-0000000009e3', 3, '00000000-0000-0000-0000-0000000009a1', 1, 0)$q$, 'add line to posted');
SELECT pg_temp.expect_fail($q$UPDATE journal_entries SET description = 'x' WHERE journal_entry_id = '00000000-0000-0000-0000-0000000009e3'$q$, 'edit posted header');
SELECT pg_temp.expect_fail($q$DELETE FROM journal_entries WHERE journal_entry_id = '00000000-0000-0000-0000-0000000009e3'$q$, 'delete posted');
SELECT pg_temp.expect_fail($q$UPDATE journal_entries SET status = 0 WHERE journal_entry_id = '00000000-0000-0000-0000-0000000009e3'$q$, 'unpost');
UPDATE journal_entries SET status = 2 WHERE journal_entry_id = '00000000-0000-0000-0000-0000000009e3';
DELETE FROM journal_entries WHERE journal_entry_id = '00000000-0000-0000-0000-0000000009e1';
ROLLBACK;
