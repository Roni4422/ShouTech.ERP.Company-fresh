-- Migration: 026_journal_integrity (PostgreSQL)
-- Mirrors dba/mig/sqlite/026_journal_integrity.sql:
--   * posting requires lines and total debit = total credit
--   * posted/reversed headers and lines are immutable and cannot be deleted
--   * status moves only draft(0) -> posted(1) -> reversed(2)

CREATE OR REPLACE FUNCTION shouttech_journal_entry_guard() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 0 THEN
            RAISE EXCEPTION 'Posted or reversed journal entries cannot be deleted; create a reversal instead';
        END IF;
        RETURN OLD;
    END IF;

    IF NEW.status <> OLD.status THEN
        IF NOT ((OLD.status = 0 AND NEW.status = 1) OR (OLD.status = 1 AND NEW.status = 2)) THEN
            RAISE EXCEPTION 'Invalid journal status transition: only draft to posted and posted to reversed are allowed';
        END IF;
        IF NEW.status = 1 THEN
            IF NOT EXISTS (SELECT 1 FROM journal_entry_lines WHERE journal_entry_id = NEW.journal_entry_id) THEN
                RAISE EXCEPTION 'Journal entry must contain at least one line before posting';
            END IF;
            IF (SELECT ROUND(COALESCE(SUM(debit), 0), 6) - ROUND(COALESCE(SUM(credit), 0), 6)
                FROM journal_entry_lines WHERE journal_entry_id = NEW.journal_entry_id) <> 0 THEN
                RAISE EXCEPTION 'Journal entry is not balanced: total debit must equal total credit';
            END IF;
        END IF;
    END IF;

    IF OLD.status <> 0 AND (
        NEW.entry_date IS DISTINCT FROM OLD.entry_date OR NEW.description IS DISTINCT FROM OLD.description
        OR NEW.company_id IS DISTINCT FROM OLD.company_id OR NEW.fiscal_period_id IS DISTINCT FROM OLD.fiscal_period_id
        OR NEW.created_by IS DISTINCT FROM OLD.created_by) THEN
        RAISE EXCEPTION 'Posted or reversed journal entries cannot be modified';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_journal_entry_guard ON journal_entries;
CREATE TRIGGER trg_journal_entry_guard
BEFORE UPDATE OR DELETE ON journal_entries
FOR EACH ROW EXECUTE FUNCTION shouttech_journal_entry_guard();

CREATE OR REPLACE FUNCTION shouttech_journal_line_guard() RETURNS trigger AS $$
DECLARE s smallint;
BEGIN
    SELECT status INTO s FROM journal_entries
    WHERE journal_entry_id = CASE WHEN TG_OP = 'INSERT' THEN NEW.journal_entry_id ELSE OLD.journal_entry_id END;
    IF COALESCE(s, 0) <> 0 THEN
        RAISE EXCEPTION 'Lines of a posted or reversed journal entry cannot be added, modified or deleted';
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_journal_line_guard ON journal_entry_lines;
CREATE TRIGGER trg_journal_line_guard
BEFORE INSERT OR UPDATE OR DELETE ON journal_entry_lines
FOR EACH ROW EXECUTE FUNCTION shouttech_journal_line_guard();
