"""Verifies database-level journal invariants (migration 026) on a fully migrated SQLite database.

Run: python3 -I tests/sql/test_journal_integrity.py
"""
import os
import sqlite3
import sys

sys.path.insert(0, os.path.dirname(__file__))
from test_financial_views import migrate  # noqa: E402


def build() -> sqlite3.Connection:
    db = migrate()
    db.execute("INSERT INTO companies(company_id, company_code, company_name_ar, company_name_en) VALUES ('c1','C1','ش','Co')")
    for aid, code, kind in [("cash", "1000", "ASSET"), ("rev", "4000", "REVENUE")]:
        db.execute("INSERT INTO chart_of_accounts(account_id, company_id, account_code, account_name, account_type) VALUES (?, 'c1', ?, ?, ?)", (aid, code, code, kind))
    db.execute("INSERT INTO fiscal_years(company_id, fiscal_year, database_name, start_date, end_date) VALUES ('c1', 2026, '', '2026-01-01', '2026-12-31')")
    db.execute("""INSERT INTO fiscal_periods(fiscal_period_id, company_id, fiscal_year_id, period_number, period_name, start_date, end_date)
                  SELECT 'p3', 'c1', fiscal_year_id, 3, '2026-03', '2026-03-01', '2026-03-31' FROM fiscal_years""")
    return db


def draft(db, eid, lines):
    db.execute("INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id) VALUES (?, 'c1', '2026-03-10', 'x', 0, 'u', 'p3')", (eid,))
    for n, (acc, dr, cr) in enumerate(lines, 1):
        db.execute("INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit) VALUES (?, ?, ?, ?, ?)", (eid, n, acc, dr, cr))


def rejected(db, sql, params=()):
    try:
        db.execute(sql, params)
    except sqlite3.IntegrityError as exc:
        return str(exc)
    except sqlite3.Error as exc:  # RAISE(ABORT) surfaces as IntegrityError; anything else is a test failure
        raise AssertionError(f"unexpected error type for {sql!r}: {exc!r}")
    return None


def main() -> int:
    db = build()

    draft(db, "unbal", [("cash", 100, 0), ("rev", 0, 90)])
    assert rejected(db, "UPDATE journal_entries SET status=1 WHERE journal_entry_id='unbal'"), "unbalanced entry was posted"
    # Fixing the lines of the draft makes it postable.
    db.execute("UPDATE journal_entry_lines SET credit=100 WHERE journal_entry_id='unbal' AND line_number=2")
    assert rejected(db, "UPDATE journal_entries SET status=1 WHERE journal_entry_id='unbal'") is None

    draft(db, "empty", [])
    assert rejected(db, "UPDATE journal_entries SET status=1 WHERE journal_entry_id='empty'"), "entry without lines was posted"

    # Six-decimal amounts balance exactly.
    draft(db, "dec", [("cash", 0.000001, 0), ("rev", 0, 0.000001)])
    assert rejected(db, "UPDATE journal_entries SET status=1 WHERE journal_entry_id='dec'") is None

    # Posted entries are immutable.
    for label, sql in [
        ("edit line", "UPDATE journal_entry_lines SET debit=5 WHERE journal_entry_id='unbal' AND line_number=1"),
        ("delete line", "DELETE FROM journal_entry_lines WHERE journal_entry_id='unbal' AND line_number=2"),
        ("add line", "INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit) VALUES ('unbal', 3, 'cash', 1, 0)"),
        ("delete entry", "DELETE FROM journal_entries WHERE journal_entry_id='unbal'"),
        ("edit description", "UPDATE journal_entries SET description='changed' WHERE journal_entry_id='unbal'"),
        ("edit date", "UPDATE journal_entries SET entry_date='2026-04-01' WHERE journal_entry_id='unbal'"),
        ("un-post", "UPDATE journal_entries SET status=0 WHERE journal_entry_id='unbal'"),
    ]:
        assert rejected(db, sql), f"posted entry allowed: {label}"

    # Reversal keeps history: posted -> reversed is allowed once, nothing moves afterwards.
    assert rejected(db, "UPDATE journal_entries SET status=2, reversed_by='u', reversed_at=CURRENT_TIMESTAMP WHERE journal_entry_id='unbal'") is None
    assert rejected(db, "UPDATE journal_entries SET status=1 WHERE journal_entry_id='unbal'"), "reversed entry was re-posted"
    assert rejected(db, "UPDATE journal_entries SET status=0 WHERE journal_entry_id='unbal'"), "reversed entry became draft"
    assert rejected(db, "DELETE FROM journal_entries WHERE journal_entry_id='unbal'"), "reversed entry was deleted"
    draft(db, "never_posted", [("cash", 1, 0), ("rev", 0, 1)])
    assert rejected(db, "UPDATE journal_entries SET status=2 WHERE journal_entry_id='never_posted'"), "draft was reversed without posting"

    # Drafts stay editable and deletable.
    draft(db, "d2", [("cash", 5, 0), ("rev", 0, 5)])
    assert rejected(db, "UPDATE journal_entry_lines SET debit=6 WHERE journal_entry_id='d2' AND line_number=1") is None
    assert rejected(db, "DELETE FROM journal_entry_lines WHERE journal_entry_id='d2'") is None
    assert rejected(db, "DELETE FROM journal_entries WHERE journal_entry_id='d2'") is None

    # Every posted/reversed entry in the ledger balances.
    bad = db.execute("""SELECT e.journal_entry_id FROM journal_entries e JOIN journal_entry_lines l ON l.journal_entry_id=e.journal_entry_id
                        WHERE e.status IN (1,2) GROUP BY e.journal_entry_id HAVING ROUND(SUM(l.debit-l.credit), 6) <> 0""").fetchall()
    assert bad == [], bad
    print("OK: journal integrity invariants verified")
    return 0


if __name__ == "__main__":
    sys.exit(main())
