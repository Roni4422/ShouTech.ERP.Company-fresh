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

    # --- Year-end closing entry (same SQL as AccountingStructureRepository.PostYearEndClosingEntryAsync) ---
    db.execute("INSERT INTO chart_of_accounts(account_id, company_id, account_code, account_name, account_type) VALUES ('re','c1','3000','Retained','EQUITY')")
    year_id = db.execute("SELECT fiscal_year_id FROM fiscal_years WHERE company_id='c1'").fetchone()[0]
    # The pre-close P&L check must see the reversed pair (status 2 + 1) as zero revenue, 40 expense.
    unclosed = db.execute(
        "SELECT COUNT(*) FROM (SELECT a.account_id FROM fiscal_years fy JOIN journal_entries e ON e.company_id=fy.company_id "
        "AND e.entry_date BETWEEN fy.start_date AND fy.end_date AND e.status IN (1,2) "
        "JOIN journal_entry_lines l ON l.journal_entry_id=e.journal_entry_id JOIN chart_of_accounts a ON a.account_id=l.account_id "
        "AND a.company_id=e.company_id WHERE fy.fiscal_year_id=? AND a.account_type IN ('REVENUE','EXPENSE') "
        "GROUP BY a.account_id HAVING ABS(SUM(l.debit-l.credit)) > 0.0001) x", (year_id,)).fetchone()[0]
    assert unclosed == 1, unclosed  # only the expense account remains; reversed revenue nets to zero

    balances = db.execute(
        "SELECT v.account_id, SUM(v.debit - v.credit) FROM v_posted_gl_lines v "
        "JOIN fiscal_years fy ON fy.company_id=v.company_id AND v.entry_date >= fy.start_date AND v.entry_date <= fy.end_date "
        "WHERE fy.fiscal_year_id=? AND v.account_type IN ('REVENUE','EXPENSE') "
        "GROUP BY v.account_id HAVING ABS(SUM(v.debit - v.credit)) > 0.0001", (year_id,)).fetchall()
    assert balances == [("exp", 40)], balances
    period = db.execute("SELECT fiscal_period_id FROM fiscal_periods WHERE fiscal_year_id=? AND status='OPEN' ORDER BY period_number DESC LIMIT 1", (year_id,)).fetchone()[0]
    db.execute(
        "INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id, document_number) "
        "SELECT 'close1', fp.company_id, fp.end_date, 'Year-end closing entry', 0, 'u', fp.fiscal_period_id, 'YE-CLOSE-1' "
        "FROM fiscal_periods fp WHERE fp.fiscal_period_id=?", (period,))
    net = 0
    for n, (acc, bal) in enumerate(balances, 1):
        net += bal
        db.execute("INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit, note) VALUES ('close1', ?, ?, ?, ?, 'Year-end closing')",
                   (n, acc, 0 if bal > 0 else -bal, bal if bal > 0 else 0))
    db.execute("INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit, note) VALUES ('close1', ?, 're', ?, ?, 'Year-end closing')",
               (len(balances) + 1, net if net > 0 else 0, 0 if net > 0 else -net))
    db.execute("UPDATE journal_entries SET status=1 WHERE journal_entry_id='close1' AND status=0")  # fires the posting triggers
    left = db.execute("SELECT COUNT(*) FROM v_posted_gl_lines WHERE account_type IN ('REVENUE','EXPENSE') GROUP BY account_id HAVING ABS(SUM(debit-credit)) > 0.0001").fetchall()
    assert left == [], left
    re_bal = db.execute("SELECT SUM(debit-credit) FROM v_posted_gl_lines WHERE account_id='re'").fetchone()[0]
    assert re_bal == 40, re_bal  # the 40 expense (a loss) is a debit to retained earnings
    # Trial balance stays balanced and a second close finds nothing left to close.
    dr, cr = db.execute("SELECT SUM(debit), SUM(credit) FROM v_posted_gl_lines").fetchone()
    assert dr == cr, (dr, cr)
    # Balance sheet: assets equal liabilities + equity once earnings are closed (cash is -40 in this fixture).
    assets = db.execute("SELECT COALESCE(SUM(natural_amount),0) FROM v_posted_gl_lines WHERE account_type='ASSET'").fetchone()[0]
    eq = db.execute("SELECT COALESCE(SUM(natural_amount),0) FROM v_posted_gl_lines WHERE account_type IN ('LIABILITY','EQUITY')").fetchone()[0]
    assert assets == eq == -40, (assets, eq)

    # --- Invoice posting (same SQL statements as GlPost.PostDocument; @name becomes :name for sqlite3) ---
    def q(sql):
        return sql.replace("@", ":")

    db.execute("INSERT INTO companies(company_id, company_code, company_name_ar, company_name_en) VALUES ('c2','C2','ش','Co2')")
    for aid, code, kind in [("c2rec", "1100", "ASSET"), ("c2inv", "1200", "ASSET"), ("c2tax", "2100", "LIABILITY"),
                            ("c2rev", "4000", "REVENUE"), ("c2cog", "5000", "EXPENSE")]:
        db.execute("INSERT INTO chart_of_accounts(account_id, company_id, account_code, account_name, account_type) VALUES (?, 'c2', ?, ?, ?)", (aid, code, code, kind))
    db.execute("INSERT INTO fiscal_years(company_id, fiscal_year, database_name, start_date, end_date) VALUES ('c2', 2026, '', '2026-01-01', '2026-12-31')")
    db.execute("INSERT INTO fiscal_periods(fiscal_period_id, company_id, fiscal_year_id, period_number, period_name, start_date, end_date) "
               "SELECT 'c2p', company_id, fiscal_year_id, 1, '2026', '2026-01-01', '2026-12-31' FROM fiscal_years WHERE company_id='c2'")
    try:
        db.execute("INSERT INTO account_mappings(company_id, mapping_key, account_id) VALUES ('c2', 'BOGUS', 'c2rec')")
        raise AssertionError("account_mappings accepted an unknown key")
    except sqlite3.IntegrityError:
        pass
    for key, acc in [("RECEIVABLES", "c2rec"), ("SALES_REVENUE", "c2rev"), ("OUTPUT_TAX", "c2tax"),
                     ("INVENTORY", "c2inv"), ("COST_OF_GOODS_SOLD", "c2cog")]:
        db.execute("INSERT INTO account_mappings(company_id, mapping_key, account_id) VALUES ('c2', ?, ?)", (key, acc))
    mapped = dict(db.execute(q("SELECT mapping_key, account_id FROM account_mappings WHERE company_id=@company"), {"company": "c2"}).fetchall())
    assert len(mapped) == 5 and mapped["RECEIVABLES"] == "c2rec", mapped

    period = db.execute(q("SELECT fp.fiscal_period_id FROM fiscal_periods fp JOIN fiscal_years fy ON fy.fiscal_year_id=fp.fiscal_year_id "
                          "WHERE fp.company_id=@company AND @date BETWEEN fp.start_date AND fp.end_date AND fp.status='OPEN' AND fy.is_closed=0"),
                        {"company": "c2", "date": "2026-03-10"}).fetchone()
    assert period == ("c2p",), period
    assert db.execute(q("SELECT fp.fiscal_period_id FROM fiscal_periods fp JOIN fiscal_years fy ON fy.fiscal_year_id=fp.fiscal_year_id "
                        "WHERE fp.company_id=@company AND @date BETWEEN fp.start_date AND fp.end_date AND fp.status='OPEN' AND fy.is_closed=0"),
                      {"company": "c2", "date": "2027-01-05"}).fetchone() is None

    db.execute(q("INSERT INTO journal_entries (journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id, reference_number, currency_code, exchange_rate, document_number) "
                 "VALUES (@id, @company, @date, @description, 0, @actor, @period, @reference, @currency, @rate, @documentNumber)"),
               {"id": "inv1", "company": "c2", "date": "2026-03-10", "description": "SAL SAL-2026-0001", "actor": "u",
                "period": "c2p", "reference": "SAL-2026-0001", "currency": "SAR", "rate": "1", "documentNumber": "GL:SAL:SAL-2026-0001"})
    plan = [("c2rec", "23.0000", "0"), ("c2rev", "0", "20.0000"), ("c2tax", "0", "3.0000"), ("c2cog", "8.0000", "0"), ("c2inv", "0", "8.0000")]
    for n, (acc, dr, cr) in enumerate(plan, 1):
        db.execute(q("INSERT INTO journal_entry_lines (journal_entry_id, line_number, account_id, debit, credit, note) VALUES (@id, @line, @account, @debit, @credit, @note)"),
                   {"id": "inv1", "line": n, "account": acc, "debit": dr, "credit": cr, "note": "n"})
    db.execute(q("INSERT INTO journal_audit_events (journal_entry_id, event_type, actor, details) VALUES (@id, 1, @actor, @details)"),
               {"id": "inv1", "actor": "u", "details": "Created"})
    cur = db.execute(q("UPDATE journal_entries SET status=1, posted_by=@actor, posted_at=CURRENT_TIMESTAMP WHERE journal_entry_id=@id AND status=0"),
                     {"id": "inv1", "actor": "u"})
    assert cur.rowcount == 1
    db.execute(q("INSERT INTO journal_audit_events (journal_entry_id, event_type, actor, details) VALUES (@id, 2, @actor, @details)"),
               {"id": "inv1", "actor": "u", "details": "Posted"})
    # The posting trigger must reject a second entry that uses an account from another company.
    try:
        db.execute("INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id) VALUES ('bad','c2','2026-03-11','x',0,'u','c2p')")
        db.execute("INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit) VALUES ('bad',1,'cash',5,0),('bad',2,'c2rev',0,5)")
        db.execute("UPDATE journal_entries SET status=1 WHERE journal_entry_id='bad'")
        raise AssertionError("posting accepted another company's account")
    except sqlite3.IntegrityError:
        pass
    rev = db.execute("SELECT SUM(natural_amount) FROM v_posted_gl_lines WHERE company_id='c2' AND account_code='4000'").fetchone()[0]
    assert rev == 20, rev
    # Document numbers are unique per company, so the same invoice cannot be posted twice.
    try:
        db.execute("INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id, document_number) VALUES ('dup','c2','2026-03-10','x',0,'u','c2p','GL:SAL:SAL-2026-0001')")
        raise AssertionError("duplicate document number accepted")
    except sqlite3.IntegrityError:
        pass
    print("OK: financial report view verified against all SQLite migrations")
    return 0


if __name__ == "__main__":
    sys.exit(main())
