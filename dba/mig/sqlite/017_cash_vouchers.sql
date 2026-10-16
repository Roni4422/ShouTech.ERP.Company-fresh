-- DBA-owned receipt/payment voucher schema. A posted voucher is linked to its GL journal.
CREATE TABLE IF NOT EXISTS cash_vouchers (
    cash_voucher_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    voucher_kind TEXT NOT NULL CHECK (voucher_kind IN ('RECEIPT','PAYMENT')),
    voucher_number TEXT NOT NULL CHECK (length(trim(voucher_number)) BETWEEN 1 AND 100),
    voucher_date TEXT NOT NULL,
    treasury_account_id TEXT NOT NULL,
    party_name TEXT,
    total_amount NUMERIC(19,4) NOT NULL CHECK (total_amount > 0),
    currency_code TEXT NOT NULL CHECK (length(trim(currency_code)) = 3),
    exchange_rate NUMERIC(19,8) NOT NULL CHECK (exchange_rate > 0),
    reference_number TEXT,
    cost_center TEXT,
    memo TEXT,
    fiscal_period_id TEXT NOT NULL,
    journal_entry_id TEXT,
    reversal_journal_entry_id TEXT,
    status INTEGER NOT NULL DEFAULT 0 CHECK (status IN (0,1,2)),
    created_by TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    posted_by TEXT,
    posted_at TEXT,
    reversed_by TEXT,
    reversed_at TEXT,
    UNIQUE(company_id, voucher_kind, voucher_number),
    FOREIGN KEY(company_id) REFERENCES companies(company_id),
    FOREIGN KEY(treasury_account_id) REFERENCES chart_of_accounts(account_id),
    FOREIGN KEY(fiscal_period_id) REFERENCES fiscal_periods(fiscal_period_id),
    FOREIGN KEY(journal_entry_id) REFERENCES journal_entries(journal_entry_id),
    FOREIGN KEY(reversal_journal_entry_id) REFERENCES journal_entries(journal_entry_id)
);

CREATE TABLE IF NOT EXISTS cash_voucher_lines (
    cash_voucher_line_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    cash_voucher_id TEXT NOT NULL,
    line_number INTEGER NOT NULL,
    account_id TEXT NOT NULL,
    amount NUMERIC(19,4) NOT NULL CHECK (amount > 0),
    cost_center TEXT,
    invoice_reference TEXT,
    note TEXT,
    UNIQUE(cash_voucher_id, line_number),
    FOREIGN KEY(cash_voucher_id) REFERENCES cash_vouchers(cash_voucher_id),
    FOREIGN KEY(account_id) REFERENCES chart_of_accounts(account_id)
);

CREATE TABLE IF NOT EXISTS cash_voucher_audit_events (
    cash_voucher_audit_event_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    cash_voucher_id TEXT NOT NULL,
    event_type INTEGER NOT NULL CHECK (event_type IN (1,2,3)),
    actor TEXT NOT NULL,
    event_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    details TEXT,
    FOREIGN KEY(cash_voucher_id) REFERENCES cash_vouchers(cash_voucher_id)
);

CREATE INDEX IF NOT EXISTS ix_cash_vouchers_company_date
    ON cash_vouchers(company_id, voucher_date, status);
CREATE INDEX IF NOT EXISTS ix_cash_voucher_lines_account
    ON cash_voucher_lines(account_id, cash_voucher_id);
CREATE INDEX IF NOT EXISTS ix_cash_voucher_audit_events_voucher
    ON cash_voucher_audit_events(cash_voucher_id, event_at);
