-- Cheque batch header and versioned detail rows. Superseded lines are retained for audit.
CREATE TABLE IF NOT EXISTS cheque_batches (
    batch_id UUID NOT NULL PRIMARY KEY,
    company_id UUID NOT NULL,
    batch_code VARCHAR(50) NOT NULL,
    batch_date DATE NOT NULL,
    cash_box_code VARCHAR(100) NOT NULL DEFAULT '',
    currency_code VARCHAR(10) NOT NULL,
    memo VARCHAR(1000) NOT NULL DEFAULT '',
    status VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL,
    created_by VARCHAR(255),
    modified_at TIMESTAMPTZ,
    modified_by VARCHAR(255),
    deleted_at TIMESTAMPTZ,
    deleted_by VARCHAR(255),
    CONSTRAINT uq_cheque_batches_company_code UNIQUE (company_id, batch_code),
    CONSTRAINT fk_cheque_batches_company FOREIGN KEY (company_id) REFERENCES companies(company_id)
);

CREATE INDEX IF NOT EXISTS ix_cheque_batches_company_date
    ON cheque_batches(company_id, batch_date, is_deleted);

CREATE TABLE IF NOT EXISTS cheque_batch_lines (
    line_id UUID NOT NULL PRIMARY KEY,
    company_id UUID NOT NULL,
    batch_id UUID NOT NULL,
    line_no INTEGER NOT NULL CHECK (line_no > 0),
    cheque_no VARCHAR(100) NOT NULL,
    bank_name VARCHAR(200) NOT NULL,
    branch_name VARCHAR(100) NOT NULL DEFAULT '',
    due_date DATE NOT NULL,
    amount NUMERIC(19,4) NOT NULL CHECK (amount > 0),
    drawer VARCHAR(200) NOT NULL DEFAULT '',
    account_code VARCHAR(80) NOT NULL DEFAULT '',
    memo VARCHAR(500) NOT NULL DEFAULT '',
    is_current BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL,
    created_by VARCHAR(255),
    superseded_at TIMESTAMPTZ,
    superseded_by VARCHAR(255),
    CONSTRAINT fk_cheque_batch_lines_company FOREIGN KEY (company_id) REFERENCES companies(company_id),
    CONSTRAINT fk_cheque_batch_lines_batch FOREIGN KEY (batch_id) REFERENCES cheque_batches(batch_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_cheque_batch_lines_current_no
    ON cheque_batch_lines(batch_id, line_no) WHERE is_current = TRUE;
CREATE INDEX IF NOT EXISTS ix_cheque_batch_lines_company_batch
    ON cheque_batch_lines(company_id, batch_id, is_current);
CREATE INDEX IF NOT EXISTS ix_cheque_batch_lines_cheque_no
    ON cheque_batch_lines(company_id, cheque_no);
