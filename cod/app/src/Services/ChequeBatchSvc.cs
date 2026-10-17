#nullable enable

using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.App;
using ShouTech.App.Localization;

namespace ShouTech.App.Services;

public sealed record ChequeBatchLineRecord
{
    public int LineNumber { get; init; }
    public string ChequeNumber { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public DateTime DueDate { get; init; }
    public decimal Amount { get; init; }
    public string Drawer { get; init; } = string.Empty;
    public string AccountCode { get; init; } = string.Empty;
    public string Memo { get; init; } = string.Empty;
}

public sealed record ChequeBatchRecord
{
    public Guid BatchId { get; init; }
    public string Code { get; init; } = string.Empty;
    public DateTime BatchDate { get; init; }
    public string CashBoxCode { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = string.Empty;
    public string Memo { get; init; } = string.Empty;
    public IReadOnlyList<ChequeBatchLineRecord> Lines { get; init; } = Array.Empty<ChequeBatchLineRecord>();
}

public sealed class ChequeBatchException : InvalidOperationException
{
    public string ErrorCode { get; }

    public ChequeBatchException(string errorCode, string message) : base(message)
        => ErrorCode = errorCode;
}

/// <summary>
/// Company-scoped draft cheque-batch persistence for SQLite and PostgreSQL. Header and lines commit
/// atomically; previous line versions are marked as superseded and retained for audit.
/// </summary>
public static class ChequeBatchSvc
{
    private static bool IsPostgreSql => DatabaseRuntime.IsPostgreSql();
    private static string ActiveCompanyCode => AppState.CurrentCompanyCode?.Trim() ?? string.Empty;
    private static string FalseLiteral => IsPostgreSql ? "FALSE" : "0";
    private static string TrueLiteral => IsPostgreSql ? "TRUE" : "1";
    private static string FalseValueLiteral => IsPostgreSql ? "FALSE" : "0";
    private static readonly Regex CurrencyPattern = new("^[A-Za-z]{3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<ChequeBatchRecord?> LoadAsync(
        string code, CancellationToken cancellationToken = default)
    {
        ValidateCode(code);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var companyId = await ResolveCompanyIdAsync(connection, null, cancellationToken).ConfigureAwait(false);

        Guid batchId;
        DateTime batchDate;
        string cashBoxCode, currencyCode, memo;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT batch_id, batch_date, cash_box_code, currency_code, memo
                FROM cheque_batches
                WHERE company_id=@company AND batch_code=@code AND is_deleted={FalseLiteral};
                """;
            Add(command, "@company", companyId);
            Add(command, "@code", code.Trim());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            batchId = ReadGuid(reader, 0);
            batchDate = ReadDate(reader, 1);
            cashBoxCode = ReadString(reader, 2);
            currencyCode = ReadString(reader, 3);
            memo = ReadString(reader, 4);
        }

        var lines = new List<ChequeBatchLineRecord>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT line_no, cheque_no, bank_name, branch_name, due_date, amount,
                       drawer, account_code, memo
                FROM cheque_batch_lines
                WHERE company_id=@company AND batch_id=@batchId AND is_current={TrueLiteral}
                ORDER BY line_no;
                """;
            Add(command, "@company", companyId);
            Add(command, "@batchId", FormatId(batchId));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lines.Add(new ChequeBatchLineRecord
                {
                    LineNumber = ReadInt32(reader, 0),
                    ChequeNumber = ReadString(reader, 1),
                    BankName = ReadString(reader, 2),
                    Branch = ReadString(reader, 3),
                    DueDate = ReadDate(reader, 4),
                    Amount = ReadDecimal(reader, 5),
                    Drawer = ReadString(reader, 6),
                    AccountCode = ReadString(reader, 7),
                    Memo = ReadString(reader, 8)
                });
            }
        }

        return new ChequeBatchRecord
        {
            BatchId = batchId,
            Code = code.Trim(),
            BatchDate = batchDate,
            CashBoxCode = cashBoxCode,
            CurrencyCode = currencyCode,
            Memo = memo,
            Lines = lines.AsReadOnly()
        };
    }

    public static async Task<Guid> SaveAsync(
        ChequeBatchRecord batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Validate(batch);
        var actor = GetActor();
        var normalizedLines = batch.Lines
            .OrderBy(line => line.LineNumber)
            .Select((line, index) => line with { LineNumber = index + 1 })
            .ToArray();

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var companyId = await ResolveCompanyIdAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            object? existingBatchId = null;
            bool isDeleted = false;
            string status = "DRAFT";

            await using (var find = connection.CreateCommand())
            {
                find.Transaction = transaction;
                find.CommandText = """
                    SELECT batch_id, is_deleted, status FROM cheque_batches
                    WHERE company_id=@company AND batch_code=@code;
                    """;
                Add(find, "@company", companyId);
                Add(find, "@code", batch.Code.Trim());
                await using var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    existingBatchId = reader.GetValue(0);
                    isDeleted = Convert.ToBoolean(reader.GetValue(1), CultureInfo.InvariantCulture);
                    status = ReadString(reader, 2, "DRAFT");
                }
            }

            if (isDeleted)
                throw new ChequeBatchException(ErrorCodes.ChequeCodeRequiresRestore,
                    "This batch code belongs to a deleted record. Restore it from the Recycle Bin before reusing the code.");
            if (existingBatchId is not null && !string.Equals(status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                throw new ChequeBatchException(ErrorCodes.ChequePostedEditRejected,
                    "This cheque batch is no longer a draft and cannot be edited. Use the approved accounting reversal workflow.");

            var now = TimestampValue();
            Guid batchId;
            if (existingBatchId is null)
            {
                batchId = Guid.NewGuid();
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO cheque_batches
                    (batch_id, company_id, batch_code, batch_date, cash_box_code, currency_code, memo,
                     status, is_active, is_deleted, created_at, created_by, modified_at, modified_by)
                    VALUES
                    (@id, @company, @code, @batchDate, @cashBoxCode, @currencyCode, @memo,
                     'DRAFT', @isActive, @isDeleted, @createdAt, @createdBy, NULL, NULL);
                    """;
                Add(insert, "@id", FormatId(batchId));
                Add(insert, "@company", companyId);
                AddBatch(insert, batch, includeCode: true);
                Add(insert, "@isActive", true);
                Add(insert, "@isDeleted", false);
                Add(insert, "@createdAt", now);
                Add(insert, "@createdBy", actor);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                batchId = IsPostgreSql && existingBatchId is Guid value
                    ? value
                    : Guid.Parse(Convert.ToString(existingBatchId, CultureInfo.InvariantCulture)!);
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = $"""
                    UPDATE cheque_batches SET
                        batch_date=@batchDate, cash_box_code=@cashBoxCode, currency_code=@currencyCode,
                        memo=@memo, modified_at=@modifiedAt, modified_by=@modifiedBy
                    WHERE company_id=@company AND batch_id=@id AND status='DRAFT' AND is_deleted={FalseLiteral};
                    """;
                Add(update, "@company", companyId);
                Add(update, "@id", FormatId(batchId));
                AddBatch(update, batch, includeCode: false);
                Add(update, "@modifiedAt", now);
                Add(update, "@modifiedBy", actor);
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The cheque batch changed during save. Reload it and try again.");

                // Preserve the previous line version as audit history; only the current version is loaded by the UI.
                await using var supersede = connection.CreateCommand();
                supersede.Transaction = transaction;
                supersede.CommandText = $"""
                    UPDATE cheque_batch_lines
                    SET is_current={FalseValueLiteral}, superseded_at=@supersededAt, superseded_by=@supersededBy
                    WHERE company_id=@company AND batch_id=@batchId AND is_current={TrueLiteral};
                    """;
                Add(supersede, "@company", companyId);
                Add(supersede, "@batchId", FormatId(batchId));
                Add(supersede, "@supersededAt", now);
                Add(supersede, "@supersededBy", actor);
                await supersede.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            for (var index = 0; index < normalizedLines.Length; index++)
            {
                var line = normalizedLines[index];
                await using var insertLine = connection.CreateCommand();
                insertLine.Transaction = transaction;
                insertLine.CommandText = """
                    INSERT INTO cheque_batch_lines
                    (line_id, company_id, batch_id, line_no, cheque_no, bank_name, branch_name,
                     due_date, amount, drawer, account_code, memo, is_current, created_at, created_by)
                    VALUES
                    (@id, @company, @batchId, @lineNo, @chequeNo, @bankName, @branchName,
                     @dueDate, @amount, @drawer, @accountCode, @memo, @isCurrent, @createdAt, @createdBy);
                    """;
                Add(insertLine, "@id", FormatId(Guid.NewGuid()));
                Add(insertLine, "@company", companyId);
                Add(insertLine, "@batchId", FormatId(batchId));
                Add(insertLine, "@lineNo", line.LineNumber);
                Add(insertLine, "@chequeNo", line.ChequeNumber.Trim());
                Add(insertLine, "@bankName", line.BankName.Trim());
                Add(insertLine, "@branchName", line.Branch.Trim());
                Add(insertLine, "@dueDate", DateValue(line.DueDate));
                Add(insertLine, "@amount", line.Amount);
                Add(insertLine, "@drawer", line.Drawer.Trim());
                Add(insertLine, "@accountCode", line.AccountCode.Trim());
                Add(insertLine, "@memo", line.Memo.Trim());
                Add(insertLine, "@isCurrent", true);
                Add(insertLine, "@createdAt", now);
                Add(insertLine, "@createdBy", actor);
                await insertLine.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            AppHost.Logging?.LogOperation("ChequeBatch", "Save",
                $"Company={ActiveCompanyCode}; Code={batch.Code.Trim()}; BatchId={batchId:D}; LineCount={normalizedLines.Length}; Outcome=Committed");
            return batchId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<bool> DeleteAsync(
        string code, CancellationToken cancellationToken = default)
    {
        ValidateCode(code);
        var actor = GetActor();
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var companyId = await ResolveCompanyIdAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE cheque_batches SET is_deleted={TrueLiteral}, deleted_at=@deletedAt, deleted_by=@deletedBy,
                    modified_at=@modifiedAt, modified_by=@modifiedBy
                WHERE company_id=@company AND batch_code=@code AND is_deleted={FalseLiteral} AND status='DRAFT';
                """;
            Add(command, "@company", companyId);
            Add(command, "@code", code.Trim());
            Add(command, "@deletedAt", TimestampValue());
            Add(command, "@deletedBy", actor);
            Add(command, "@modifiedAt", TimestampValue());
            Add(command, "@modifiedBy", actor);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            AppHost.Logging?.LogOperation("ChequeBatch", "SoftDelete",
                $"Company={ActiveCompanyCode}; Code={code.Trim()}; Outcome=Committed");
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static void Validate(ChequeBatchRecord batch)
    {
        ValidateCode(batch.Code);
        if (batch.BatchDate == DateTime.MinValue)
            throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, "A batch date is required.");
        if (batch.CashBoxCode.Trim().Length > 100)
            throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, "The cash-box code cannot exceed 100 characters.");
        if (!CurrencyPattern.IsMatch(batch.CurrencyCode.Trim()))
            throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, "Enter a valid three-letter currency code such as JOD, USD, or SAR.");
        if (batch.Memo.Trim().Length > 1000)
            throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, "Batch memo cannot exceed 1000 characters.");
        if (batch.Lines is null || batch.Lines.Count is < 1 or > 500)
            throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, "A batch must contain between 1 and 500 cheque lines.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < batch.Lines.Count; index++)
        {
            var line = batch.Lines[index];
            if (line is null)
                throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, "Cheque lines cannot be empty.");
            if (string.IsNullOrWhiteSpace(line.ChequeNumber) || line.ChequeNumber.Trim().Length > 100)
                throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, $"Enter a cheque number with no more than 100 characters on line {index + 1}.");
            if (string.IsNullOrWhiteSpace(line.BankName) || line.BankName.Trim().Length > 200)
                throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, $"Enter a bank name with no more than 200 characters on line {index + 1}.");
            if (line.Branch.Trim().Length > 100 || line.Drawer.Trim().Length > 200 ||
                line.AccountCode.Trim().Length > 80 || line.Memo.Trim().Length > 500)
                throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, $"One or more fields exceed their supported length on line {index + 1}.");
            if (line.DueDate == DateTime.MinValue)
                throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, $"A due date is required on line {index + 1}.");
            if (line.Amount <= 0 || line.Amount > 999999999999999.9999m || decimal.Round(line.Amount, 4) != line.Amount)
                throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, $"The cheque amount must be positive and use at most four decimal places on line {index + 1}.");

            var chequeKey = line.BankName.Trim() + "|" + line.ChequeNumber.Trim();
            if (!seen.Add(chequeKey))
                throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed,
                    $"Duplicate cheque number '{line.ChequeNumber.Trim()}' for bank '{line.BankName.Trim()}' in this batch.");
        }
    }

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50)
            throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, "Enter a batch code with no more than 50 characters.");
        if (string.IsNullOrWhiteSpace(ActiveCompanyCode))
            throw new ChequeBatchException(ErrorCodes.ChequeContextFailed, "Select an active company before using the cheque batch.");
    }

    private static string GetActor()
    {
        var actor = AppState.CurrentUser?.Username?.Trim();
        if (string.IsNullOrWhiteSpace(actor))
            actor = AppHost.Session.CurrentUser?.Trim();
        if (string.IsNullOrWhiteSpace(actor))
            throw new ChequeBatchException(ErrorCodes.ChequeContextFailed, "The current user identity could not be resolved.");
        return actor;
    }

    private static async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = DatabaseRuntime.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ChequeBatchException(ErrorCodes.ChequeContextFailed, "The active database connection is not configured.");

        DbConnection connection = IsPostgreSql
            ? new NpgsqlConnection(connectionString)
            : new SqliteConnection(DatabaseRuntime.NormalizeSqliteConnectionString(connectionString));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<object> ResolveCompanyIdAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ActiveCompanyCode))
            throw new ChequeBatchException(ErrorCodes.ChequeContextFailed, "Select an active company before using the cheque batch.");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT company_id FROM companies
            WHERE company_code=@code AND is_active={TrueLiteral} AND is_deleted={FalseLiteral}
            LIMIT 1;
            """;
        Add(command, "@code", ActiveCompanyCode);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull)
            throw new ChequeBatchException(ErrorCodes.ChequeContextFailed, "The active company is not registered in the connected database.");

        if (IsPostgreSql)
        {
            if (value is Guid guid) return guid;
            if (Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)) return parsed;
            throw new ChequeBatchException(ErrorCodes.ChequeContextFailed, "The active company identifier is invalid.");
        }

        var sqliteCompanyId = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(sqliteCompanyId)) return sqliteCompanyId;
        throw new ChequeBatchException(ErrorCodes.ChequeContextFailed, "The active company identifier is invalid.");
    }

    private static void AddBatch(DbCommand command, ChequeBatchRecord batch, bool includeCode)
    {
        if (includeCode)
            Add(command, "@code", batch.Code.Trim());
        Add(command, "@batchDate", DateValue(batch.BatchDate));
        Add(command, "@cashBoxCode", batch.CashBoxCode.Trim());
        Add(command, "@currencyCode", batch.CurrencyCode.Trim().ToUpperInvariant());
        Add(command, "@memo", batch.Memo.Trim());
    }

    private static object FormatId(Guid value) =>
        IsPostgreSql ? value : value.ToString("D", CultureInfo.InvariantCulture);

    private static object DateValue(DateTime value) =>
        IsPostgreSql ? DateOnly.FromDateTime(value.Date) : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static object TimestampValue() =>
        IsPostgreSql ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static Guid ReadGuid(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        if (value is Guid guid) return guid;
        if (Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)) return parsed;
        throw new InvalidOperationException("The stored cheque-batch identifier is invalid.");
    }

    private static DateTime ReadDate(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly date => date.ToDateTime(TimeOnly.MinValue),
            DateTime date => date.Date,
            _ => DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).Date
        };
    }

    private static int ReadInt32(DbDataReader reader, int ordinal) =>
        Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static decimal ReadDecimal(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static string ReadString(DbDataReader reader, int ordinal, string fallback = "") =>
        reader.IsDBNull(ordinal) ? fallback :
        Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? fallback;

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
