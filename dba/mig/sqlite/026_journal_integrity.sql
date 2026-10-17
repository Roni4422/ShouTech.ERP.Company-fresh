-- Migration: 026_journal_integrity
-- Description: Database-level accounting invariants for journal entries.
--   * a journal entry can only be posted when it has lines and total debit = total credit
--   * lines of posted/reversed entries are immutable (add/edit/delete are rejected)
--   * status moves only draft(0) -> posted(1) -> reversed(2)
--   * posted/reversed entries keep their date, description, company and fiscal period
--   * posted/reversed entries cannot be deleted

CREATE TRIGGER IF NOT EXISTS trg_journal_post_requires_balance
BEFORE UPDATE OF status ON journal_entries
WHEN NEW.status = 1 AND OLD.status <> 1
BEGIN
    SELECT CASE WHEN NOT EXISTS (
        SELECT 1 FROM journal_entry_lines WHERE journal_entry_id = NEW.journal_entry_id
    ) THEN RAISE(ABORT, 'Journal entry must contain at least one line before posting') END;
    SELECT CASE WHEN (
        SELECT ROUND(COALESCE(SUM(debit), 0), 6) - ROUND(COALESCE(SUM(credit), 0), 6)
        FROM journal_entry_lines WHERE journal_entry_id = NEW.journal_entry_id
    ) <> 0 THEN RAISE(ABORT, 'Journal entry is not balanced: total debit must equal total credit') END;
END;

CREATE TRIGGER IF NOT EXISTS trg_journal_status_transition
BEFORE UPDATE OF status ON journal_entries
WHEN NEW.status <> OLD.status AND NOT (
    (OLD.status = 0 AND NEW.status = 1) OR (OLD.status = 1 AND NEW.status = 2)
)
BEGIN
    SELECT RAISE(ABORT, 'Invalid journal status transition: only draft to posted and posted to reversed are allowed');
END;

CREATE TRIGGER IF NOT EXISTS trg_journal_posted_header_immutable
BEFORE UPDATE OF entry_date, description, company_id, fiscal_period_id, created_by ON journal_entries
WHEN OLD.status <> 0 AND (
    NEW.entry_date IS NOT OLD.entry_date OR NEW.description IS NOT OLD.description
    OR NEW.company_id IS NOT OLD.company_id OR NEW.fiscal_period_id IS NOT OLD.fiscal_period_id
    OR NEW.created_by IS NOT OLD.created_by
)
BEGIN
    SELECT RAISE(ABORT, 'Posted or reversed journal entries cannot be modified');
END;

CREATE TRIGGER IF NOT EXISTS trg_journal_posted_no_delete
BEFORE DELETE ON journal_entries
WHEN OLD.status <> 0
BEGIN
    SELECT RAISE(ABORT, 'Posted or reversed journal entries cannot be deleted; create a reversal instead');
END;

CREATE TRIGGER IF NOT EXISTS trg_journal_lines_no_insert_when_posted
BEFORE INSERT ON journal_entry_lines
WHEN COALESCE((SELECT status FROM journal_entries WHERE journal_entry_id = NEW.journal_entry_id), 0) <> 0
BEGIN
    SELECT RAISE(ABORT, 'Lines cannot be added to a posted or reversed journal entry');
END;

CREATE TRIGGER IF NOT EXISTS trg_journal_lines_no_update_when_posted
BEFORE UPDATE ON journal_entry_lines
WHEN COALESCE((SELECT status FROM journal_entries WHERE journal_entry_id = OLD.journal_entry_id), 0) <> 0
BEGIN
    SELECT RAISE(ABORT, 'Lines of a posted or reversed journal entry cannot be modified');
END;

CREATE TRIGGER IF NOT EXISTS trg_journal_lines_no_delete_when_posted
BEFORE DELETE ON journal_entry_lines
WHEN COALESCE((SELECT status FROM journal_entries WHERE journal_entry_id = OLD.journal_entry_id), 0) <> 0
BEGIN
    SELECT RAISE(ABORT, 'Lines of a posted or reversed journal entry cannot be deleted');
END;
