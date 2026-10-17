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

-- Invoice posting: same statements as GlPost.PostDocument, with literals for the @parameters.
INSERT INTO companies(company_id, company_code, company_name_ar, company_name_en)
VALUES ('00000000-0000-0000-0000-0000000000c2', 'T2', 'ش', 'Test Co 2');
INSERT INTO chart_of_accounts(account_id, company_id, account_code, account_name, account_type) VALUES
 ('00000000-0000-0000-0000-0000000b0001', '00000000-0000-0000-0000-0000000000c2', '1100', 'Receivables', 'ASSET'),
 ('00000000-0000-0000-0000-0000000b0002', '00000000-0000-0000-0000-0000000000c2', '1200', 'Inventory', 'ASSET'),
 ('00000000-0000-0000-0000-0000000b0003', '00000000-0000-0000-0000-0000000000c2', '2100', 'Output tax', 'LIABILITY'),
 ('00000000-0000-0000-0000-0000000b0004', '00000000-0000-0000-0000-0000000000c2', '4000', 'Revenue', 'REVENUE'),
 ('00000000-0000-0000-0000-0000000b0005', '00000000-0000-0000-0000-0000000000c2', '5000', 'COGS', 'EXPENSE');
INSERT INTO fiscal_years(company_id, fiscal_year, database_name, start_date, end_date)
VALUES ('00000000-0000-0000-0000-0000000000c2', 2026, '', '2026-01-01', '2026-12-31');
INSERT INTO fiscal_periods(fiscal_period_id, company_id, fiscal_year_id, period_number, period_name, start_date, end_date)
SELECT '00000000-0000-0000-0000-0000000000f4', company_id, fiscal_year_id, 1, '2026', '2026-01-01', '2026-12-31'
FROM fiscal_years WHERE company_id = '00000000-0000-0000-0000-0000000000c2';

INSERT INTO account_mappings(company_id, mapping_key, account_id) VALUES
 ('00000000-0000-0000-0000-0000000000c2', 'RECEIVABLES', '00000000-0000-0000-0000-0000000b0001'),
 ('00000000-0000-0000-0000-0000000000c2', 'INVENTORY', '00000000-0000-0000-0000-0000000b0002'),
 ('00000000-0000-0000-0000-0000000000c2', 'OUTPUT_TAX', '00000000-0000-0000-0000-0000000b0003'),
 ('00000000-0000-0000-0000-0000000000c2', 'SALES_REVENUE', '00000000-0000-0000-0000-0000000b0004'),
 ('00000000-0000-0000-0000-0000000000c2', 'COST_OF_GOODS_SOLD', '00000000-0000-0000-0000-0000000b0005');

DO $$
BEGIN
  BEGIN
    INSERT INTO account_mappings(company_id, mapping_key, account_id)
    VALUES ('00000000-0000-0000-0000-0000000000c2', 'BOGUS', '00000000-0000-0000-0000-0000000b0001');
    RAISE EXCEPTION 'account_mappings accepted an unknown key';
  EXCEPTION WHEN check_violation THEN NULL;
  END;
  IF (SELECT count(*) FROM account_mappings WHERE company_id = '00000000-0000-0000-0000-0000000000c2') <> 5 THEN
    RAISE EXCEPTION 'mapping lookup returned the wrong number of rows';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM fiscal_periods fp JOIN fiscal_years fy ON fy.fiscal_year_id = fp.fiscal_year_id
                 WHERE fp.company_id = '00000000-0000-0000-0000-0000000000c2' AND DATE '2026-03-10' BETWEEN fp.start_date AND fp.end_date
                   AND fp.status = 'OPEN' AND fy.is_closed = false) THEN
    RAISE EXCEPTION 'open period lookup failed';
  END IF;
  IF EXISTS (SELECT 1 FROM fiscal_periods fp JOIN fiscal_years fy ON fy.fiscal_year_id = fp.fiscal_year_id
             WHERE fp.company_id = '00000000-0000-0000-0000-0000000000c2' AND DATE '2027-01-05' BETWEEN fp.start_date AND fp.end_date
               AND fp.status = 'OPEN' AND fy.is_closed = false) THEN
    RAISE EXCEPTION 'a date outside the fiscal year found a period';
  END IF;
END $$;

INSERT INTO journal_entries (journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id, reference_number, currency_code, exchange_rate, document_number)
VALUES ('00000000-0000-0000-0000-0000000000e5', '00000000-0000-0000-0000-0000000000c2', DATE '2026-03-10', 'SAL SAL-2026-0001', 0, 'u',
        '00000000-0000-0000-0000-0000000000f4', 'SAL-2026-0001', 'SAR', 1, 'GL:SAL:SAL-2026-0001');
INSERT INTO journal_entry_lines (journal_entry_id, line_number, account_id, debit, credit, note) VALUES
 ('00000000-0000-0000-0000-0000000000e5', 1, '00000000-0000-0000-0000-0000000b0001', 23.0000, 0, 'n'),
 ('00000000-0000-0000-0000-0000000000e5', 2, '00000000-0000-0000-0000-0000000b0004', 0, 20.0000, 'n'),
 ('00000000-0000-0000-0000-0000000000e5', 3, '00000000-0000-0000-0000-0000000b0003', 0, 3.0000, 'n'),
 ('00000000-0000-0000-0000-0000000000e5', 4, '00000000-0000-0000-0000-0000000b0005', 8.0000, 0, 'n'),
 ('00000000-0000-0000-0000-0000000000e5', 5, '00000000-0000-0000-0000-0000000b0002', 0, 8.0000, 'n');
INSERT INTO journal_audit_events (journal_entry_id, event_type, actor, details)
VALUES ('00000000-0000-0000-0000-0000000000e5', 1, 'u', 'Created');
UPDATE journal_entries SET status = 1, posted_by = 'u', posted_at = CURRENT_TIMESTAMP
WHERE journal_entry_id = '00000000-0000-0000-0000-0000000000e5' AND status = 0;
INSERT INTO journal_audit_events (journal_entry_id, event_type, actor, details)
VALUES ('00000000-0000-0000-0000-0000000000e5', 2, 'u', 'Posted');

DO $$
DECLARE rev numeric; dr numeric; cr numeric;
BEGIN
  SELECT sum(natural_amount) INTO rev FROM v_posted_gl_lines
   WHERE company_id = '00000000-0000-0000-0000-0000000000c2' AND account_code = '4000';
  IF rev <> 20 THEN RAISE EXCEPTION 'invoice revenue wrong: %', rev; END IF;
  SELECT sum(debit), sum(credit) INTO dr, cr FROM v_posted_gl_lines
   WHERE company_id = '00000000-0000-0000-0000-0000000000c2';
  IF dr <> 31 OR cr <> 31 THEN RAISE EXCEPTION 'invoice entry unbalanced: % / %', dr, cr; END IF;
  -- Another company's account must be rejected by the posting trigger.
  BEGIN
    INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id)
    VALUES ('00000000-0000-0000-0000-0000000000e6', '00000000-0000-0000-0000-0000000000c2', DATE '2026-03-11', 'x', 0, 'u', '00000000-0000-0000-0000-0000000000f4');
    INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit) VALUES
     ('00000000-0000-0000-0000-0000000000e6', 1, '00000000-0000-0000-0000-00000000a001', 5, 0),
     ('00000000-0000-0000-0000-0000000000e6', 2, '00000000-0000-0000-0000-0000000b0004', 0, 5);
    UPDATE journal_entries SET status = 1 WHERE journal_entry_id = '00000000-0000-0000-0000-0000000000e6';
    RAISE EXCEPTION 'posting accepted another company''s account';
  EXCEPTION WHEN raise_exception THEN
    IF SQLERRM LIKE 'posting accepted%' THEN RAISE; END IF;
  END;
  -- A duplicate document number must be rejected.
  BEGIN
    INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id, document_number)
    VALUES ('00000000-0000-0000-0000-0000000000e7', '00000000-0000-0000-0000-0000000000c2', DATE '2026-03-10', 'x', 0, 'u',
            '00000000-0000-0000-0000-0000000000f4', 'GL:SAL:SAL-2026-0001');
    RAISE EXCEPTION 'duplicate document number accepted';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
END $$;
ROLLBACK;
SELECT 'OK: PostgreSQL financial report view verified' AS result;
