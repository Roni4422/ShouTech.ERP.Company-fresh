using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.Domain.Entities;
using ShouTech.Domain.Interfaces;

namespace ShouTech.Infrastructure;

/// <summary>
/// Provider-neutral receipt/payment persistence. Draft creation, posting to the general ledger,
/// and reversal are each atomic transactions; a posted voucher is never overwritten or deleted.
/// </summary>
public sealed class CashVoucherRepository : ICashVoucherRepository
{
    private readonly string _provider;
    private readonly string _connectionString;

    public CashVoucherRepository(string provider, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A database connection string is required.", nameof(connectionString));
        _provider = NormalizeProvider(provider);
        _connectionString = connectionString;
    }

    public async Task<Guid> ResolveCompanyIdAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyCode))
            throw new ArgumentException("Company code is required.", nameof(companyCode));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT company_id FROM companies WHERE company_code=@code AND is_active=" +
            (_provider == "PostgreSQL" ? "true" : "1") + " AND is_deleted=" +
            (_provider == "PostgreSQL" ? "false" : "0");
        Add(command, "@code", companyCode.Trim());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull || !Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var companyId) || companyId == Guid.Empty)
            throw new InvalidOperationException($"Active company '{companyCode.Trim()}' could not be resolved in the current database.");
        return companyId;
    }

    public async Task<Guid> ResolveAccountIdAsync(Guid companyId, string accountCode, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(accountCode)) throw new ArgumentException("Account code is required.", nameof(accountCode));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT account_id FROM chart_of_accounts WHERE company_id=@company AND account_code=@code AND {PostablePredicate("is_active", "is_deleted", "is_postable")}";
        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@code", accountCode.Trim());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull || !Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var accountId) || accountId == Guid.Empty)
            throw new InvalidOperationException($"Account code '{accountCode.Trim()}' is missing, inactive, deleted, or not postable for this company.");
        return accountId;
    }

    public async Task<string> ResolveAccountNameAsync(Guid companyId, string accountCode, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(accountCode)) throw new ArgumentException("Account code is required.", nameof(accountCode));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT account_name FROM chart_of_accounts WHERE company_id=@company AND account_code=@code AND {PostablePredicate("is_active", "is_deleted", "is_postable")}";
        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@code", accountCode.Trim());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var name = value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"Account code '{accountCode.Trim()}' could not be resolved to an active, postable account.");
        return name;
    }

    public async Task<Guid> SaveDraftAsync(Guid companyId, CashVoucher voucher, string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User is required.", nameof(userId));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureActiveCompanyAsync(connection, transaction, companyId, cancellationToken).ConfigureAwait(false);
            var periodId = await GetOpenPeriodIdAsync(connection, transaction, companyId, voucher.VoucherDate, cancellationToken).ConfigureAwait(false);
            await EnsurePostableAccountAsync(connection, transaction, companyId, voucher.TreasuryAccountId, "Treasury/bank", cancellationToken, requireAsset: true).ConfigureAwait(false);
            foreach (var line in voucher.Lines)
                await EnsurePostableAccountAsync(connection, transaction, companyId, line.AccountId, "Allocation", cancellationToken).ConfigureAwait(false);

            await using (var duplicate = CreateCommand(connection, transaction,
                "SELECT COUNT(*) FROM cash_vouchers WHERE company_id=@company AND voucher_kind=@kind AND voucher_number=@number"))
            {
                Add(duplicate, "@company", FormatGuid(companyId));
                Add(duplicate, "@kind", KindCode(voucher.Kind));
                Add(duplicate, "@number", voucher.VoucherNumber);
                if (Convert.ToInt64(await duplicate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException($"Voucher number '{voucher.VoucherNumber}' already exists for this company and voucher type.");
            }

            await using (var header = CreateCommand(connection, transaction, """
                INSERT INTO cash_vouchers
                    (cash_voucher_id, company_id, voucher_kind, voucher_number, voucher_date, treasury_account_id,
                     party_name, total_amount, currency_code, exchange_rate, reference_number, cost_center, memo,
                     fiscal_period_id, status, created_by)
                VALUES
                    (@id, @company, @kind, @number, @date, @treasury, @party, @amount, @currency, @rate,
                     @reference, @costCenter, @memo, @period, @status, @actor)
                """))
            {
                Add(header, "@id", FormatGuid(voucher.Id));
                Add(header, "@company", FormatGuid(companyId));
                Add(header, "@kind", KindCode(voucher.Kind));
                Add(header, "@number", voucher.VoucherNumber);
                Add(header, "@date", DateValue(voucher.VoucherDate));
                Add(header, "@treasury", FormatGuid(voucher.TreasuryAccountId));
                Add(header, "@party", DbValue(voucher.PartyName));
                Add(header, "@amount", voucher.TotalAmount);
                Add(header, "@currency", voucher.CurrencyCode);
                Add(header, "@rate", voucher.ExchangeRate);
                Add(header, "@reference", DbValue(voucher.ReferenceNumber));
                Add(header, "@costCenter", DbValue(voucher.CostCenter));
                Add(header, "@memo", DbValue(voucher.Memo));
                Add(header, "@period", FormatGuid(periodId));
                Add(header, "@status", (int)CashVoucherStatus.Draft);
                Add(header, "@actor", userId.Trim());
                await header.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var lineNumber = 0;
            foreach (var line in voucher.Lines)
            {
                await using var insertLine = CreateCommand(connection, transaction, """
                    INSERT INTO cash_voucher_lines
                        (cash_voucher_id, line_number, account_id, amount, cost_center, invoice_reference, note)
                    VALUES (@id, @line, @account, @amount, @costCenter, @invoiceReference, @note)
                    """);
                Add(insertLine, "@id", FormatGuid(voucher.Id));
                Add(insertLine, "@line", ++lineNumber);
                Add(insertLine, "@account", FormatGuid(line.AccountId));
                Add(insertLine, "@amount", line.Amount);
                Add(insertLine, "@costCenter", DbValue(line.CostCenter ?? voucher.CostCenter));
                Add(insertLine, "@invoiceReference", DbValue(line.InvoiceReference));
                Add(insertLine, "@note", DbValue(line.Note));
                await insertLine.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await AddVoucherAuditAsync(connection, transaction, voucher.Id, 1, userId, "Draft created", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return voucher.Id;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CashVoucherSnapshot?> FindByDocumentNumberAsync(
        Guid companyId,
        CashVoucherKind kind,
        string voucherNumber,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(voucherNumber)) throw new ArgumentException("Voucher number is required.", nameof(voucherNumber));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        CashVoucherHeader? header;
        string treasuryCode;
        string treasuryName;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT v.cash_voucher_id, v.company_id, v.voucher_kind, v.voucher_date, v.voucher_number,
                       v.treasury_account_id, v.party_name, v.total_amount, v.currency_code, v.exchange_rate,
                       v.reference_number, v.cost_center, v.memo, v.fiscal_period_id, v.journal_entry_id,
                       v.reversal_journal_entry_id, v.status, a.account_code, a.account_name
                FROM cash_vouchers v
                LEFT JOIN chart_of_accounts a
                  ON a.account_id=v.treasury_account_id AND a.company_id=v.company_id
                WHERE v.company_id=@company AND v.voucher_kind=@kind AND v.voucher_number=@number
                """;
            Add(command, "@company", FormatGuid(companyId));
            Add(command, "@kind", KindCode(kind));
            Add(command, "@number", voucherNumber.Trim());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            if (reader.IsDBNull(17) || reader.IsDBNull(18))
                throw new InvalidOperationException("The voucher treasury/bank account is missing from the company chart of accounts.");
            header = ReadHeader(reader);
            treasuryCode = reader.GetString(17);
            treasuryName = reader.GetString(18);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Duplicate voucher numbers were found; voucher loading was stopped to protect data integrity.");
        }

        var lines = await ReadVoucherLinesAsync(connection, transaction: null, header.Id, companyId, cancellationToken).ConfigureAwait(false);
        if (lines.Count == 0)
            throw new InvalidOperationException("The saved voucher has no allocation lines and requires integrity review.");

        return new CashVoucherSnapshot(
            header.Id,
            header.CompanyId,
            header.Kind,
            header.VoucherDate,
            header.VoucherNumber,
            header.TreasuryAccountId,
            treasuryCode,
            treasuryName,
            header.PartyName,
            header.TotalAmount,
            header.CurrencyCode,
            header.ExchangeRate,
            header.ReferenceNumber,
            header.CostCenter,
            header.Memo,
            header.FiscalPeriodId,
            header.JournalEntryId,
            header.ReversalJournalEntryId,
            header.Status,
            lines);
    }

    public async Task<Guid> PostAsync(Guid companyId, CashVoucherKind kind, Guid voucherId, string userId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        if (voucherId == Guid.Empty) throw new ArgumentException("Voucher is required.", nameof(voucherId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User is required.", nameof(userId));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var header = await ReadVoucherHeaderAsync(connection, transaction, companyId, kind, voucherId, forUpdate: true, cancellationToken).ConfigureAwait(false);
            if (header.Status != CashVoucherStatus.Draft || header.JournalEntryId.HasValue)
                throw new InvalidOperationException("Only an unposted draft voucher can be posted.");
            var lines = await ReadVoucherLinesForPostingAsync(connection, transaction, voucherId, cancellationToken).ConfigureAwait(false);
            ValidateStoredVoucher(header, lines);
            await EnsureOpenPeriodAsync(connection, transaction, companyId, header.FiscalPeriodId, header.VoucherDate, cancellationToken).ConfigureAwait(false);
            await EnsurePostableAccountAsync(connection, transaction, companyId, header.TreasuryAccountId, "Treasury/bank", cancellationToken, requireAsset: true).ConfigureAwait(false);
            foreach (var line in lines)
                await EnsurePostableAccountAsync(connection, transaction, companyId, line.AccountId, "Allocation", cancellationToken).ConfigureAwait(false);

            var journalId = Guid.NewGuid();
            var journalNumber = $"CV-{(kind == CashVoucherKind.Receipt ? "R" : "P")}-{voucherId:N}";
            var journalDescription = BuildJournalDescription(header);
            await using (var journal = CreateCommand(connection, transaction, """
                INSERT INTO journal_entries
                    (journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id,
                     reference_number, currency_code, exchange_rate, document_number)
                VALUES (@id, @company, @date, @description, @status, @actor, @period,
                        @reference, @currency, @rate, @number)
                """))
            {
                Add(journal, "@id", FormatGuid(journalId));
                Add(journal, "@company", FormatGuid(companyId));
                Add(journal, "@date", DateValue(header.VoucherDate));
                Add(journal, "@description", journalDescription);
                Add(journal, "@status", (int)JournalEntryStatus.Draft);
                Add(journal, "@actor", userId.Trim());
                Add(journal, "@period", FormatGuid(header.FiscalPeriodId));
                Add(journal, "@reference", header.ReferenceNumber ?? header.VoucherNumber);
                Add(journal, "@currency", header.CurrencyCode);
                Add(journal, "@rate", header.ExchangeRate);
                Add(journal, "@number", journalNumber);
                await journal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var lineNumber = 0;
            var treasuryDebit = kind == CashVoucherKind.Receipt ? header.TotalAmount : 0m;
            var treasuryCredit = kind == CashVoucherKind.Payment ? header.TotalAmount : 0m;
            await InsertJournalLineAsync(
                connection, transaction, journalId, ++lineNumber, header.TreasuryAccountId,
                treasuryDebit, treasuryCredit, header.CostCenter,
                $"Cash voucher {header.VoucherNumber}", cancellationToken).ConfigureAwait(false);

            foreach (var line in lines)
            {
                var debit = kind == CashVoucherKind.Payment ? line.Amount : 0m;
                var credit = kind == CashVoucherKind.Receipt ? line.Amount : 0m;
                var note = BuildLineNote(line);
                await InsertJournalLineAsync(
                    connection, transaction, journalId, ++lineNumber, line.AccountId, debit, credit,
                    line.CostCenter ?? header.CostCenter, note, cancellationToken).ConfigureAwait(false);
            }

            await AddJournalAuditAsync(connection, transaction, journalId, 1, userId, $"Created from cash voucher {header.VoucherNumber}", cancellationToken).ConfigureAwait(false);
            await using (var postJournal = CreateCommand(connection, transaction,
                "UPDATE journal_entries SET status=@posted, posted_by=@actor, posted_at=CURRENT_TIMESTAMP WHERE journal_entry_id=@id AND status=@draft"))
            {
                Add(postJournal, "@posted", (int)JournalEntryStatus.Posted);
                Add(postJournal, "@actor", userId.Trim());
                Add(postJournal, "@id", FormatGuid(journalId));
                Add(postJournal, "@draft", (int)JournalEntryStatus.Draft);
                if (await postJournal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The linked journal could not be posted; the cash voucher was not posted.");
            }
            await AddJournalAuditAsync(connection, transaction, journalId, 2, userId, $"Posted from cash voucher {header.VoucherNumber}", cancellationToken).ConfigureAwait(false);

            await using (var update = CreateCommand(connection, transaction, """
                UPDATE cash_vouchers
                SET status=@posted, posted_by=@actor, posted_at=CURRENT_TIMESTAMP, journal_entry_id=@journal
                WHERE cash_voucher_id=@id AND company_id=@company AND voucher_kind=@kind
                  AND status=@draft AND journal_entry_id IS NULL
                """))
            {
                Add(update, "@posted", (int)CashVoucherStatus.Posted);
                Add(update, "@actor", userId.Trim());
                Add(update, "@journal", FormatGuid(journalId));
                Add(update, "@id", FormatGuid(voucherId));
                Add(update, "@company", FormatGuid(companyId));
                Add(update, "@kind", KindCode(kind));
                Add(update, "@draft", (int)CashVoucherStatus.Draft);
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The cash voucher changed while it was being posted.");
            }

            await AddVoucherAuditAsync(connection, transaction, voucherId, 2, userId, $"Posted to journal {journalId:D}", cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return journalId;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<Guid> ReverseAsync(Guid companyId, CashVoucherKind kind, Guid voucherId, string userId, string reason, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        if (voucherId == Guid.Empty) throw new ArgumentException("Voucher is required.", nameof(voucherId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reversal reason is required.", nameof(reason));
        var details = reason.Trim();
        if (details.Length > 900)
            throw new ArgumentException("Reversal reason cannot exceed 900 characters.", nameof(reason));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var header = await ReadVoucherHeaderAsync(connection, transaction, companyId, kind, voucherId, forUpdate: true, cancellationToken).ConfigureAwait(false);
            if (header.Status != CashVoucherStatus.Posted || !header.JournalEntryId.HasValue || header.ReversalJournalEntryId.HasValue)
                throw new InvalidOperationException("Only a posted, not-yet-reversed voucher can be reversed.");
            await EnsureOpenPeriodAsync(connection, transaction, companyId, header.FiscalPeriodId, header.VoucherDate, cancellationToken).ConfigureAwait(false);

            Guid sourceJournalId = header.JournalEntryId.Value;
            DateOnly journalDate;
            string description;
            string? reference;
            string currency;
            decimal rate;
            string periodId;
            int status;
            await using (var source = CreateCommand(connection, transaction,
                "SELECT entry_date, description, status, fiscal_period_id, reference_number, currency_code, exchange_rate FROM journal_entries WHERE journal_entry_id=@id AND company_id=@company"))
            {
                Add(source, "@id", FormatGuid(sourceJournalId));
                Add(source, "@company", FormatGuid(companyId));
                await using var reader = await source.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException("The linked posted journal was not found for this company.");
                journalDate = ReadDate(reader, 0);
                description = reader.GetString(1);
                status = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture);
                periodId = Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty;
                reference = reader.IsDBNull(4) ? null : reader.GetString(4);
                currency = reader.GetString(5).Trim();
                rate = Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture);
            }
            if (status != (int)JournalEntryStatus.Posted)
                throw new InvalidOperationException("The linked general-ledger journal is not posted and cannot be reversed safely.");
            if (!Guid.TryParse(periodId, out var sourcePeriodId) || sourcePeriodId != header.FiscalPeriodId)
                throw new InvalidOperationException("The voucher and linked journal fiscal periods do not match.");

            var sourceLines = await ReadJournalLinesAsync(connection, transaction, sourceJournalId, cancellationToken).ConfigureAwait(false);
            if (sourceLines.Count < 2 || sourceLines.Sum(line => line.Debit) <= 0 ||
                sourceLines.Sum(line => line.Debit) != sourceLines.Sum(line => line.Credit))
                throw new InvalidOperationException("The linked journal is not balanced and cannot be reversed safely.");

            var reversalId = Guid.NewGuid();
            var reversalNumber = $"REV-{reversalId:N}";
            var reversalDescription = $"Reversal of cash voucher {header.VoucherNumber}: {details}";
            if (reversalDescription.Length > 500)
                reversalDescription = reversalDescription[..500];

            await using (var insert = CreateCommand(connection, transaction, """
                INSERT INTO journal_entries
                    (journal_entry_id, company_id, entry_date, description, status, created_by, reversal_of_entry_id,
                     fiscal_period_id, reference_number, currency_code, exchange_rate, document_number)
                VALUES (@id, @company, @date, @description, @draft, @actor, @source,
                        @period, @reference, @currency, @rate, @number)
                """))
            {
                Add(insert, "@id", FormatGuid(reversalId));
                Add(insert, "@company", FormatGuid(companyId));
                Add(insert, "@date", DateValue(journalDate));
                Add(insert, "@description", reversalDescription);
                Add(insert, "@draft", (int)JournalEntryStatus.Draft);
                Add(insert, "@actor", userId.Trim());
                Add(insert, "@source", FormatGuid(sourceJournalId));
                Add(insert, "@period", FormatGuid(sourcePeriodId));
                Add(insert, "@reference", reference ?? header.VoucherNumber);
                Add(insert, "@currency", currency);
                Add(insert, "@rate", rate);
                Add(insert, "@number", reversalNumber);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var lineNumber = 0;
            foreach (var line in sourceLines)
            {
                await InsertJournalLineAsync(connection, transaction, reversalId, ++lineNumber, line.AccountId,
                    line.Credit, line.Debit, line.CostCenter, line.Note, cancellationToken).ConfigureAwait(false);
            }
            await AddJournalAuditAsync(connection, transaction, sourceJournalId, 3, userId,
                $"Reversed by cash voucher {header.VoucherNumber}; reason={details}", cancellationToken).ConfigureAwait(false);
            await using (var post = CreateCommand(connection, transaction,
                "UPDATE journal_entries SET status=@posted, posted_by=@actor, posted_at=CURRENT_TIMESTAMP WHERE journal_entry_id=@id AND status=@draft"))
            {
                Add(post, "@posted", (int)JournalEntryStatus.Posted);
                Add(post, "@actor", userId.Trim());
                Add(post, "@id", FormatGuid(reversalId));
                Add(post, "@draft", (int)JournalEntryStatus.Draft);
                if (await post.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The reversing journal could not be posted.");
            }
            await AddJournalAuditAsync(connection, transaction, reversalId, 2, userId,
                $"Reversal of journal {sourceJournalId:D}; reason={details}", cancellationToken).ConfigureAwait(false);

            await using (var markOriginal = CreateCommand(connection, transaction,
                "UPDATE journal_entries SET status=@reversed, reversed_by=@actor, reversed_at=CURRENT_TIMESTAMP WHERE journal_entry_id=@id AND status=@posted"))
            {
                Add(markOriginal, "@reversed", (int)JournalEntryStatus.Reversed);
                Add(markOriginal, "@actor", userId.Trim());
                Add(markOriginal, "@id", FormatGuid(sourceJournalId));
                Add(markOriginal, "@posted", (int)JournalEntryStatus.Posted);
                if (await markOriginal.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The source journal changed during reversal.");
            }

            await using (var updateVoucher = CreateCommand(connection, transaction, """
                UPDATE cash_vouchers
                SET status=@reversed, reversal_journal_entry_id=@reversal, reversed_by=@actor, reversed_at=CURRENT_TIMESTAMP
                WHERE cash_voucher_id=@id AND company_id=@company AND voucher_kind=@kind
                  AND status=@posted AND reversal_journal_entry_id IS NULL
                """))
            {
                Add(updateVoucher, "@reversed", (int)CashVoucherStatus.Reversed);
                Add(updateVoucher, "@reversal", FormatGuid(reversalId));
                Add(updateVoucher, "@actor", userId.Trim());
                Add(updateVoucher, "@id", FormatGuid(voucherId));
                Add(updateVoucher, "@company", FormatGuid(companyId));
                Add(updateVoucher, "@kind", KindCode(kind));
                Add(updateVoucher, "@posted", (int)CashVoucherStatus.Posted);
                if (await updateVoucher.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The voucher changed during reversal.");
            }

            await AddVoucherAuditAsync(connection, transaction, voucherId, 3, userId,
                $"Reversed by journal {reversalId:D}; reason={details}", cancellationToken).ConfigureAwait(false);
            await CommitOrRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            return reversalId;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<CashVoucherHeader> ReadVoucherHeaderAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid companyId,
        CashVoucherKind kind,
        Guid voucherId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var lockSuffix = _provider == "PostgreSQL" && forUpdate ? " FOR UPDATE" : string.Empty;
        await using var command = CreateCommand(connection, transaction, """
            SELECT cash_voucher_id, company_id, voucher_kind, voucher_date, voucher_number, treasury_account_id,
                   party_name, total_amount, currency_code, exchange_rate, reference_number, cost_center, memo,
                   fiscal_period_id, journal_entry_id, reversal_journal_entry_id, status
            FROM cash_vouchers
            WHERE cash_voucher_id=@id AND company_id=@company AND voucher_kind=@kind
            """ + lockSuffix);
        Add(command, "@id", FormatGuid(voucherId));
        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@kind", KindCode(kind));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The cash voucher was not found for the active company and voucher type.");
        var header = ReadHeader(reader);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The voucher identifier is not unique within the selected company.");
        return header;
    }

    private CashVoucherHeader ReadHeader(DbDataReader reader)
    {
        var kindCode = reader.GetString(2);
        var kind = kindCode switch
        {
            "RECEIPT" => CashVoucherKind.Receipt,
            "PAYMENT" => CashVoucherKind.Payment,
            _ => throw new InvalidOperationException($"Unknown cash voucher type '{kindCode}'.")
        };
        var statusValue = Convert.ToInt32(reader.GetValue(16), CultureInfo.InvariantCulture);
        if (!Enum.IsDefined(typeof(CashVoucherStatus), statusValue))
            throw new InvalidOperationException($"Unknown cash voucher status '{statusValue}'.");
        return new CashVoucherHeader(
            ParseGuid(reader.GetValue(0), "cash voucher"),
            ParseGuid(reader.GetValue(1), "company"),
            kind,
            ReadDate(reader, 3),
            reader.GetString(4),
            ParseGuid(reader.GetValue(5), "treasury account"),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture),
            reader.GetString(8).Trim(),
            Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            ParseGuid(reader.GetValue(13), "fiscal period"),
            reader.IsDBNull(14) ? null : ParseGuid(reader.GetValue(14), "journal entry"),
            reader.IsDBNull(15) ? null : ParseGuid(reader.GetValue(15), "reversal journal entry"),
            (CashVoucherStatus)statusValue);
    }

    private async Task<List<CashVoucherLineSnapshot>> ReadVoucherLinesAsync(
        DbConnection connection,
        DbTransaction? transaction,
        Guid voucherId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var lines = new List<CashVoucherLineSnapshot>();
        await using var command = transaction is null
            ? connection.CreateCommand()
            : CreateCommand(connection, transaction, string.Empty);
        command.CommandText = """
            SELECT l.line_number, l.account_id, a.account_code, a.account_name,
                   l.amount, l.cost_center, l.invoice_reference, l.note
            FROM cash_voucher_lines l
            LEFT JOIN chart_of_accounts a ON a.account_id=l.account_id AND a.company_id=@company
            WHERE l.cash_voucher_id=@id
            ORDER BY l.line_number
            """;
        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@id", FormatGuid(voucherId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(2) || reader.IsDBNull(3))
                throw new InvalidOperationException($"Voucher allocation line {Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)} refers to an account missing from the selected company.");
            lines.Add(new CashVoucherLineSnapshot(
                Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                ParseGuid(reader.GetValue(1), "allocation account"),
                reader.GetString(2),
                reader.GetString(3),
                Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return lines;
    }

    private async Task<List<StoredVoucherLine>> ReadVoucherLinesForPostingAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid voucherId,
        CancellationToken cancellationToken)
    {
        var lines = new List<StoredVoucherLine>();
        await using var command = CreateCommand(connection, transaction, """
            SELECT line_number, account_id, amount, cost_center, invoice_reference, note
            FROM cash_voucher_lines WHERE cash_voucher_id=@id ORDER BY line_number
            """);
        Add(command, "@id", FormatGuid(voucherId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            lines.Add(new StoredVoucherLine(
                Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                ParseGuid(reader.GetValue(1), "allocation account"),
                Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return lines;
    }

    private async Task<List<StoredJournalLine>> ReadJournalLinesAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid journalId,
        CancellationToken cancellationToken)
    {
        var lines = new List<StoredJournalLine>();
        await using var command = CreateCommand(connection, transaction, """
            SELECT account_id, debit, credit, cost_center, note
            FROM journal_entry_lines WHERE journal_entry_id=@id ORDER BY line_number
            """);
        Add(command, "@id", FormatGuid(journalId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            lines.Add(new StoredJournalLine(
                ParseGuid(reader.GetValue(0), "journal account"),
                Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return lines;
    }

    private async Task EnsureActiveCompanyAsync(DbConnection connection, DbTransaction transaction, Guid companyId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT COUNT(*) FROM companies WHERE company_id=@company AND is_active=" +
            (_provider == "PostgreSQL" ? "true" : "1") + " AND is_deleted=" +
            (_provider == "PostgreSQL" ? "false" : "0"));
        Add(command, "@company", FormatGuid(companyId));
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("The selected company is not active in the current database.");
    }

    private async Task EnsurePostableAccountAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid companyId,
        Guid accountId,
        string role,
        CancellationToken cancellationToken,
        bool requireAsset = false)
    {
        var accountTypePredicate = requireAsset ? " AND account_type='ASSET'" : string.Empty;
        await using var command = CreateCommand(connection, transaction,
            $"SELECT COUNT(*) FROM chart_of_accounts WHERE company_id=@company AND account_id=@account AND {PostablePredicate("is_active", "is_deleted", "is_postable")}{accountTypePredicate}");
        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@account", FormatGuid(accountId));
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
        {
            var rule = requireAsset
                ? "must be an active, non-deleted, postable asset account"
                : "must be an active, non-deleted, postable account";
            throw new InvalidOperationException($"{role} account '{accountId:D}' {rule} for this company.");
        }
    }

    private async Task<Guid> GetOpenPeriodIdAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid companyId,
        DateOnly voucherDate,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT fp.fiscal_period_id FROM fiscal_periods fp JOIN fiscal_years fy ON fy.fiscal_year_id=fp.fiscal_year_id " +
            "WHERE fp.company_id=@company AND @date BETWEEN fp.start_date AND fp.end_date AND fp.status='OPEN' AND fy.is_closed=" +
            (_provider == "PostgreSQL" ? "false" : "0") + " ORDER BY fp.start_date LIMIT 1");
        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@date", DateValue(voucherDate));
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull)
            throw new InvalidOperationException("No open fiscal period matches the voucher date and company.");
        return ParseGuid(value, "fiscal period");
    }

    private async Task EnsureOpenPeriodAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid companyId,
        Guid fiscalPeriodId,
        DateOnly entryDate,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT COUNT(*) FROM fiscal_periods fp JOIN fiscal_years fy ON fy.fiscal_year_id=fp.fiscal_year_id " +
            "WHERE fp.fiscal_period_id=@period AND fp.company_id=@company AND @date BETWEEN fp.start_date AND fp.end_date " +
            "AND fp.status='OPEN' AND fy.is_closed=" + (_provider == "PostgreSQL" ? "false" : "0"));
        Add(command, "@period", FormatGuid(fiscalPeriodId));
        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@date", DateValue(entryDate));
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("The voucher fiscal period is closed or does not match its company/date; posting or reversal is blocked.");
    }

    private void ValidateStoredVoucher(CashVoucherHeader header, IReadOnlyList<StoredVoucherLine> lines)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException("The cash voucher has no allocation lines.");
        if (lines.Any(line => line.Amount <= 0 || decimal.Round(line.Amount, 4) != line.Amount))
            throw new InvalidOperationException("The cash voucher contains an invalid allocation amount.");
        if (lines.Sum(line => line.Amount) != header.TotalAmount)
            throw new InvalidOperationException("The saved voucher total differs from the sum of its allocation lines; posting was blocked.");
        if (lines.Any(line => line.AccountId == header.TreasuryAccountId))
            throw new InvalidOperationException("The allocation account cannot be the same as the treasury/bank account.");
    }

    private async Task InsertJournalLineAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid journalId,
        int lineNumber,
        Guid accountId,
        decimal debit,
        decimal credit,
        string? costCenter,
        string? note,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO journal_entry_lines
                (journal_entry_id, line_number, account_id, debit, credit, cost_center, note)
            VALUES (@id, @line, @account, @debit, @credit, @costCenter, @note)
            """);
        Add(command, "@id", FormatGuid(journalId));
        Add(command, "@line", lineNumber);
        Add(command, "@account", FormatGuid(accountId));
        Add(command, "@debit", debit);
        Add(command, "@credit", credit);
        Add(command, "@costCenter", DbValue(costCenter));
        Add(command, "@note", DbValue(note));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AddJournalAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid journalId,
        int eventType,
        string actor,
        string? details,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "INSERT INTO journal_audit_events (journal_entry_id, event_type, actor, details) VALUES (@id, @type, @actor, @details)");
        Add(command, "@id", FormatGuid(journalId));
        Add(command, "@type", eventType);
        Add(command, "@actor", actor.Trim());
        Add(command, "@details", DbValue(details));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AddVoucherAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid voucherId,
        int eventType,
        string actor,
        string? details,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "INSERT INTO cash_voucher_audit_events (cash_voucher_id, event_type, actor, details) VALUES (@id, @type, @actor, @details)");
        Add(command, "@id", FormatGuid(voucherId));
        Add(command, "@type", eventType);
        Add(command, "@actor", actor.Trim());
        Add(command, "@details", DbValue(details));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private DbConnection CreateConnection() => _provider == "SQLite"
        ? new SqliteConnection(_connectionString)
        : new NpgsqlConnection(_connectionString);

    private async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (_provider == "SQLite")
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA foreign_keys = ON";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static DbCommand CreateCommand(DbConnection connection, DbTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private object FormatGuid(Guid value) => _provider == "PostgreSQL" ? value : value.ToString("D");
    private object DateValue(DateOnly value) => _provider == "PostgreSQL" ? value : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private DateOnly ReadDate(DbDataReader reader, int ordinal) => _provider == "PostgreSQL"
        ? reader.GetFieldValue<DateOnly>(ordinal)
        : DateOnly.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);

    private static Guid ParseGuid(object value, string subject)
    {
        if (!Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var id) || id == Guid.Empty)
            throw new InvalidOperationException($"The saved {subject} identifier is invalid.");
        return id;
    }

    private string PostablePredicate(string activeColumn, string deletedColumn, string postableColumn) =>
        _provider == "PostgreSQL"
            ? $"{activeColumn}=true AND {deletedColumn}=false AND {postableColumn}=true"
            : $"{activeColumn}=1 AND {deletedColumn}=0 AND {postableColumn}=1";

    private static string KindCode(CashVoucherKind kind) => kind switch
    {
        CashVoucherKind.Receipt => "RECEIPT",
        CashVoucherKind.Payment => "PAYMENT",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string BuildJournalDescription(CashVoucherHeader header)
    {
        var descriptor = header.Kind == CashVoucherKind.Receipt ? "Receipt" : "Payment";
        var text = string.IsNullOrWhiteSpace(header.Memo)
            ? $"{descriptor} voucher {header.VoucherNumber}"
            : $"{descriptor} voucher {header.VoucherNumber}: {header.Memo}";
        return text.Length <= 500 ? text : text[..500];
    }

    private static string? BuildLineNote(StoredVoucherLine line)
    {
        var value = string.Join(" | ", new[] { line.InvoiceReference, line.Note }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return value.Length == 0 ? null : value.Length <= 500 ? value : value[..500];
    }

    private static async Task CommitOrRollbackAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "sqlite" or "sqlite3" => "SQLite",
        "postgres" or "postgresql" or "npgsql" => "PostgreSQL",
        _ => throw new NotSupportedException($"Unsupported database provider '{provider}'. Use SQLite or PostgreSQL.")
    };

    private sealed record CashVoucherHeader(
        Guid Id,
        Guid CompanyId,
        CashVoucherKind Kind,
        DateOnly VoucherDate,
        string VoucherNumber,
        Guid TreasuryAccountId,
        string? PartyName,
        decimal TotalAmount,
        string CurrencyCode,
        decimal ExchangeRate,
        string? ReferenceNumber,
        string? CostCenter,
        string? Memo,
        Guid FiscalPeriodId,
        Guid? JournalEntryId,
        Guid? ReversalJournalEntryId,
        CashVoucherStatus Status);

    private sealed record StoredVoucherLine(
        int LineNumber,
        Guid AccountId,
        decimal Amount,
        string? CostCenter,
        string? InvoiceReference,
        string? Note);

    private sealed record StoredJournalLine(Guid AccountId, decimal Debit, decimal Credit, string? CostCenter, string? Note);
}
