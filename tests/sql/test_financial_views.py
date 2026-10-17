"""Applies every SQLite migration to an in-memory database and verifies the GL reporting view.

Run: python3 -I tests/sql/test_financial_views.py
"""
import glob
import os
import sqlite3
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


DIRECTIVE = "-- @if-column-missing "


def run_migration(db: sqlite3.Connection, sql: str) -> None:
    """Mirror tools/SqlMigrator: run plain SQL in batches and honour @if-column-missing directives."""
    lines = sql.replace("\r\n", "\n").split("\n")
    pending: list[str] = []
    i = 0
    while i < len(lines):
        line = lines[i]
        if not line.strip().lower().startswith(DIRECTIVE):
            pending.append(line)
            i += 1
            continue
        db.executescript("\n".join(pending))
        pending = []
        table, column = line.strip()[len(DIRECTIVE):].strip().split(".")
        statement = []
        while i + 1 < len(lines) and not lines[i + 1].lstrip().lower().startswith(DIRECTIVE):
            i += 1
            statement.append(lines[i])
            if ";" in lines[i]:
                break
        exists = db.execute(f"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = ?", (column,)).fetchone()[0]
        if not exists:
            db.executescript("\n".join(statement))
        i += 1
    db.executescript("\n".join(pending))


def migrate() -> sqlite3.Connection:
    db = sqlite3.connect(":memory:")
    files = sorted(glob.glob(os.path.join(ROOT, "dba", "mig", "sqlite", "*.sql")), key=lambda p: os.path.basename(p).lower())
    for path in files:
        with open(path, encoding="utf-8") as handle:
            try:
                run_migration(db, handle.read())
            except sqlite3.Error as exc:
                raise SystemExit(f"Migration failed: {os.path.basename(path)}: {exc}")
    return db


def main() -> int:
    db = migrate()
    db.execute("INSERT INTO companies(company_id, company_code, company_name_ar, company_name_en) VALUES ('c1','C1','ش','Co')")
    for aid, code, kind in [("cash", "1000", "ASSET"), ("rev", "4000", "REVENUE"), ("exp", "5000", "EXPENSE")]:
        db.execute("INSERT INTO chart_of_accounts(account_id, company_id, account_code, account_name, account_type) VALUES (?, 'c1', ?, ?, ?)", (aid, code, code, kind))

    def entry(eid, status, lines):
        db.execute("INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by) VALUES (?, 'c1', '2026-03-10', 'x', 0, 'u')", (eid,))
        for n, (acc, dr, cr) in enumerate(lines, 1):
            db.execute("INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit) VALUES (?, ?, ?, ?, ?)", (eid, n, acc, dr, cr))
        if status:
            db.execute("UPDATE journal_entries SET fiscal_period_id=(SELECT fiscal_period_id FROM fiscal_periods WHERE company_id='c1' AND '2026-03-10' BETWEEN start_date AND end_date) WHERE journal_entry_id=?", (eid,))
            db.execute("UPDATE journal_entries SET status=1 WHERE journal_entry_id=?", (eid,))

    db.execute("INSERT INTO fiscal_years(company_id, fiscal_year, database_name, start_date, end_date) VALUES ('c1', 2026, '', '2026-01-01', '2026-12-31')")
    db.execute("""INSERT INTO fiscal_periods(fiscal_period_id, company_id, fiscal_year_id, period_number, period_name, start_date, end_date)
                  SELECT 'p3', 'c1', fiscal_year_id, 3, '2026-03', '2026-03-01', '2026-03-31' FROM fiscal_years WHERE company_id='c1'""")
    entry("sale", 1, [("cash", 100, 0), ("rev", 0, 100)])
    entry("cost", 1, [("exp", 40, 0), ("cash", 0, 40)])
    entry("draft", 0, [("cash", 999, 0), ("rev", 0, 999)])
    db.commit()

    rows = {r[0]: r for r in db.execute("SELECT account_id, SUM(debit), SUM(credit), SUM(natural_amount) FROM v_posted_gl_lines GROUP BY account_id")}
    assert rows["cash"][1:] == (100, 40, 60), rows["cash"]
    assert rows["rev"][3] == 100 and rows["exp"][3] == 40, rows
    total_dr, total_cr = db.execute("SELECT SUM(debit), SUM(credit) FROM v_posted_gl_lines").fetchone()
    assert total_dr == total_cr == 140, (total_dr, total_cr)

    # Reversal: original (status 2) plus reversing entry (status 1) must net to zero.
    db.execute("UPDATE journal_entries SET status=2 WHERE journal_entry_id='sale'")
    entry("sale_rev", 1, [("rev", 100, 0), ("cash", 0, 100)])
    net = db.execute("SELECT SUM(natural_amount) FROM v_posted_gl_lines WHERE account_id='rev'").fetchone()[0]
    assert net == 0, net
    print("OK: financial report view verified against all SQLite migrations")
    return 0


if __name__ == "__main__":
    sys.exit(main())
