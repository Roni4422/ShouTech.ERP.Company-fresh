-- Cheque batch header and versioned detail rows. Superseded lines are retained for audit.
CREATE TABLE IF NOT EXISTS cheque_batches (
    batch_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    batch_code TEXT NOT NULL,
    batch_date TEXT NOT NULL,
    cash_box_code TEXT NOT NULL DEFAULT '',
    currency_code TEXT NOT NULL,
    memo TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'DRAFT',
    is_active INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    created_by TEXT,
    modified_at TEXT,
    modified_by TEXT,
    deleted_at TEXT,
    deleted_by TEXT,
    CONSTRAINT uq_cheque_batches_company_code UNIQUE (company_id, batch_code),
    CONSTRAINT fk_cheque_batches_company FOREIGN KEY (company_id) REFERENCES companies(company_id)
);

CREATE INDEX IF NOT EXISTS ix_cheque_batches_company_date
    ON cheque_batches(company_id, batch_date, is_deleted);

CREATE TABLE IF NOT EXISTS cheque_batch_lines (
    line_id TEXT NOT NULL PRIMARY KEY,
    company_id TEXT NOT NULL,
    batch_id TEXT NOT NULL,
    line_no INTEGER NOT NULL CHECK (line_no > 0),
    cheque_no TEXT NOT NULL,
    bank_name TEXT NOT NULL,
    branch_name TEXT NOT NULL DEFAULT '',
    due_date TEXT NOT NULL,
    amount NUMERIC NOT NULL CHECK (amount > 0),
    drawer TEXT NOT NULL DEFAULT '',
    account_code TEXT NOT NULL DEFAULT '',
    memo TEXT NOT NULL DEFAULT '',
    is_current INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL,
    created_by TEXT,
    superseded_at TEXT,
    superseded_by TEXT,
    CONSTRAINT fk_cheque_batch_lines_company FOREIGN KEY (company_id) REFERENCES companies(company_id),
    CONSTRAINT fk_cheque_batch_lines_batch FOREIGN KEY (batch_id) REFERENCES cheque_batches(batch_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_cheque_batch_lines_current_no
    ON cheque_batch_lines(batch_id, line_no) WHERE is_current = 1;
CREATE INDEX IF NOT EXISTS ix_cheque_batch_lines_company_batch
    ON cheque_batch_lines(company_id, batch_id, is_current);
CREATE INDEX IF NOT EXISTS ix_cheque_batch_lines_cheque_no
    ON cheque_batch_lines(company_id, cheque_no);
