-- Financial reporting foundation (PostgreSQL).
-- Reversed originals (status 2) stay in the ledger together with their reversal entries (status 1),
-- so the pair nets to zero and the audit trail remains complete. Drafts (status 0) never count.
CREATE OR REPLACE VIEW v_posted_gl_lines AS
SELECT je.company_id,
       je.journal_entry_id,
       je.entry_date,
       je.fiscal_period_id,
       l.line_number,
       a.account_id,
       a.account_code,
       a.account_name,
       a.account_type,
       l.debit,
       l.credit,
       CASE WHEN a.account_type IN ('ASSET','EXPENSE') THEN l.debit - l.credit
            ELSE l.credit - l.debit END AS natural_amount,
       l.cost_center
FROM journal_entries je
JOIN journal_entry_lines l ON l.journal_entry_id = je.journal_entry_id
JOIN chart_of_accounts a ON a.account_id = l.account_id AND a.company_id = je.company_id
WHERE je.status IN (1, 2);

CREATE INDEX IF NOT EXISTS ix_journal_entries_company_date_status
    ON journal_entries(company_id, entry_date, status);
