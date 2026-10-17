"""General database checks on a fully migrated SQLite database.

Covers: migration file naming/pairing, report views queryable,
gl_account_defaults constraints, and a double-entry trial-balance reconciliation.
Run: python3 -I tests/sql/test_general.py
"""
import os
import re
import sqlite3
import sys

sys.path.insert(0, os.path.dirname(__file__))
from test_financial_views import migrate  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MIG = os.path.join(ROOT, "dba", "mig")


def files(provider):
    return sorted(f for f in os.listdir(os.path.join(MIG, provider)) if f.endswith(".sql"))


def check_migration_files():
    for provider in ("sqlite", "postgresql"):
        names = files(provider)
        assert names, provider
        assert len(names) == len(set(names)), f"duplicate names in {provider}"
        prefixes = [re.match(r"(\d+)_", n).group(1) for n in names]
        assert all(len(p) == 3 for p in prefixes), f"non zero-padded prefix in {provider}"
        # Historic migrations (< 022) share prefixes; IDs are full paths so they stay valid. New ones must be unique.
        recent = [p for p in prefixes if int(p) >= 22]
        assert len(recent) == len(set(recent)), f"duplicate numeric prefix >= 022 in {provider}"
    sq = {n[:3] for n in files("sqlite") if int(n[:3]) >= 22}
    pg = {n[:3] for n in files("postgresql") if int(n[:3]) >= 22}
    assert sq == pg, f"provider migration sets differ: {sorted(sq ^ pg)}"


def check_views(db):
    views = [r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='view' AND name LIKE 'v_rpt_%'")]
    assert len(views) >= 12, views
    for v in views:
        db.execute(f"SELECT * FROM {v} LIMIT 1").fetchall()


def check_gl_defaults(db):
    db.execute("INSERT INTO companies(company_id, company_code, company_name_ar, company_name_en) VALUES ('c1','C1','ش','Co')")
    db.execute("INSERT INTO chart_of_accounts(account_id, company_id, account_code, account_name, account_type) VALUES ('a1','c1','1000','Cash','ASSET')")
    db.execute("INSERT INTO gl_account_defaults(company_id, role, account_id) VALUES ('c1','AR','a1')")
    for sql in ("INSERT INTO gl_account_defaults(company_id, role, account_id) VALUES ('c1','BOGUS','a1')",
                "INSERT INTO gl_account_defaults(company_id, role, account_id) VALUES ('c1','AR','a1')",
                "INSERT INTO gl_account_defaults(company_id, role, account_id) VALUES ('c1','AP','missing')"):
        db.execute("PRAGMA foreign_keys=ON")
        try:
            db.execute(sql)
        except sqlite3.IntegrityError:
            continue
        raise AssertionError(f"accepted invalid default: {sql}")


def check_reconciliation(db):
    db.execute("INSERT INTO chart_of_accounts(account_id, company_id, account_code, account_name, account_type) VALUES ('r1','c1','4000','Rev','REVENUE')")
    db.execute("INSERT INTO fiscal_years(company_id, fiscal_year, database_name, start_date, end_date) VALUES ('c1',2026,'','2026-01-01','2026-12-31')")
    db.execute("""INSERT INTO fiscal_periods(fiscal_period_id, company_id, fiscal_year_id, period_number, period_name, start_date, end_date)
                  SELECT 'p3','c1',fiscal_year_id,3,'m3','2026-03-01','2026-03-31' FROM fiscal_years""")
    for i in range(1, 4):
        eid = f"e{i}"
        db.execute("INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id) VALUES (?, 'c1','2026-03-10','x',0,'u','p3')", (eid,))
        db.execute("INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit) VALUES (?,1,'a1',?,0)", (eid, 10 * i))
        db.execute("INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit) VALUES (?,2,'r1',0,?)", (eid, 10 * i))
        db.execute("UPDATE journal_entries SET status=1 WHERE journal_entry_id=?", (eid,))
    dr, cr = db.execute("""SELECT SUM(l.debit), SUM(l.credit) FROM journal_entry_lines l
                           JOIN journal_entries j ON j.journal_entry_id=l.journal_entry_id WHERE j.status=1""").fetchone()
    assert dr == cr == 60, (dr, cr)


def main() -> int:
    check_migration_files()
    db = migrate()
    check_views(db)
    check_gl_defaults(db)
    check_reconciliation(db)
    print("general tests passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
