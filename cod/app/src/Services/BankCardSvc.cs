#nullable enable

using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.App;
using ShouTech.App.Localization;

namespace ShouTech.App.Services;

public sealed record BankCardRecord
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string Iban { get; init; } = string.Empty;
    public string CurrencyCode { get; init; } = "SAR";
    public string GeneralLedgerAccountCode { get; init; } = string.Empty;
}

public sealed class BankCardException : InvalidOperationException
{
    public string ErrorCode { get; }

    public BankCardException(string errorCode, string message) : base(message)
        => ErrorCode = errorCode;
}

/// <summary>
/// Provider-neutral bank-card persistence. Every query is constrained to the active company.
/// Bank deletion is a soft-delete to preserve financial history.
/// </summary>
public static class BankCardSvc
{
    private static bool IsPostgreSql => DatabaseRuntime.IsPostgreSql();
    private static string ActiveCompanyCode => AppState.CurrentCompanyCode?.Trim() ?? string.Empty;
    private static string FalseLiteral => IsPostgreSql ? "FALSE" : "0";
    private static string TrueLiteral => IsPostgreSql ? "TRUE" : "1";
    private static readonly Regex CurrencyPattern = new("^[A-Za-z]{3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex IbanPattern = new("^[A-Za-z0-9 ]{15,34}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<BankCardRecord?> LoadByCodeAsync(
        string code, CancellationToken cancellationToken = default)
    {
        ValidateCode(code);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var companyId = await ResolveCompanyIdAsync(connection, null, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT bank_code, name, branch_name, account_number, iban, currency_code, gl_account_code
            FROM banks
            WHERE company_id=@company AND bank_code=@code AND is_deleted={FalseLiteral};
            """;
        Add(command, "@company", companyId);
        Add(command, "@code", code.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new BankCardRecord
        {
            Code = ReadString(reader, 0),
            Name = ReadString(reader, 1),
            Branch = ReadString(reader, 2),
            AccountNumber = ReadString(reader, 3),
            Iban = ReadString(reader, 4),
            CurrencyCode = ReadString(reader, 5, "SAR"),
            GeneralLedgerAccountCode = ReadString(reader, 6)
        };
    }

    public static async Task SaveAsync(BankCardRecord bank, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bank);
        Validate(bank);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var companyId = await ResolveCompanyIdAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            object? existingId = null;
            bool isDeleted = false;
            await using (var find = connection.CreateCommand())
            {
                find.Transaction = transaction;
                find.CommandText = "SELECT bank_id, is_deleted FROM banks WHERE company_id=@company AND bank_code=@code;";
                Add(find, "@company", companyId);
                Add(find, "@code", bank.Code.Trim());
                await using var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    existingId = reader.GetValue(0);
                    isDeleted = Convert.ToBoolean(reader.GetValue(1), CultureInfo.InvariantCulture);
                }
            }

            if (isDeleted)
                throw new BankCardException(ErrorCodes.BankCodeRequiresRestore,
                    "This bank code belongs to a deleted record. Restore it from the Recycle Bin before reusing the code.");

            var now = TimestampValue();
            if (existingId is null)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO banks
                    (bank_id, company_id, bank_code, name, type, gl_account_id, account_number,
                     address, phone, is_active, is_deleted, created_at, created_by, modified_at,
                     modified_by, branch_name, iban, currency_code, gl_account_code)
                    VALUES
                    (@id, @company, @code, @name, 1, NULL, @accountNumber,
                     '', '', @isActive, @isDeleted, @createdAt, @createdBy, NULL,
                     NULL, @branch, @iban, @currencyCode, @glAccountCode);
                    """;
                Add(insert, "@id", FormatId(Guid.NewGuid()));
                Add(insert, "@company", companyId);
                Bind(insert, bank);
                Add(insert, "@isActive", true);
                Add(insert, "@isDeleted", false);
                Add(insert, "@createdAt", now);
                Add(insert, "@createdBy", ActorValue());
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = $"""
                    UPDATE banks SET
                        name=@name, account_number=@accountNumber,
                        branch_name=@branch, iban=@iban, currency_code=@currencyCode,
                        gl_account_code=@glAccountCode, modified_at=@modifiedAt, modified_by=@modifiedBy
                    WHERE company_id=@company AND bank_id=@id AND is_deleted={FalseLiteral};
                    """;
                Add(update, "@company", companyId);
                Add(update, "@id", existingId);
                Bind(update, bank, includeCode: false);
                Add(update, "@modifiedAt", now);
                Add(update, "@modifiedBy", ActorValue());
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The bank record changed during the save operation. Reload it and try again.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            AppHost.Logging?.LogOperation("BankCard", "Save",
                $"Company={ActiveCompanyCode}; Code={bank.Code.Trim()}; Outcome=Committed");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<bool> DeleteAsync(string code, CancellationToken cancellationToken = default)
    {
        ValidateCode(code);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var companyId = await ResolveCompanyIdAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE banks SET is_deleted={TrueLiteral}, deleted_at=@deletedAt, deleted_by=@deletedBy,
                    modified_at=@modifiedAt, modified_by=@modifiedBy
                WHERE company_id=@company AND bank_code=@code AND is_deleted={FalseLiteral};
                """;
            Add(command, "@company", companyId);
            Add(command, "@code", code.Trim());
            Add(command, "@deletedAt", TimestampValue());
            Add(command, "@deletedBy", ActorValue());
            Add(command, "@modifiedAt", TimestampValue());
            Add(command, "@modifiedBy", ActorValue());
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            AppHost.Logging?.LogOperation("BankCard", "SoftDelete",
                $"Company={ActiveCompanyCode}; Code={code.Trim()}; Outcome=Committed");
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static void Validate(BankCardRecord bank)
    {
        ValidateCode(bank.Code);
        if (string.IsNullOrWhiteSpace(bank.Name) || bank.Name.Trim().Length > 200)
            throw new BankCardException(ErrorCodes.BankValidationFailed, "Enter a bank name with no more than 200 characters.");
        if (bank.Branch.Trim().Length > 200 || bank.AccountNumber.Trim().Length > 100 ||
            bank.GeneralLedgerAccountCode.Trim().Length > 80)
            throw new BankCardException(ErrorCodes.BankValidationFailed, "One or more bank fields exceed the supported length.");
        if (!CurrencyPattern.IsMatch(bank.CurrencyCode.Trim()))
            throw new BankCardException(ErrorCodes.BankValidationFailed, "Enter a three-letter currency code such as JOD, USD, or SAR.");
        if (!string.IsNullOrWhiteSpace(bank.Iban))
        {
            var compactIban = Regex.Replace(bank.Iban, @"\s+", string.Empty);
            if (!IbanPattern.IsMatch(bank.Iban.Trim()) || compactIban.Length is < 15 or > 34 ||
                compactIban.Any(ch => !char.IsLetterOrDigit(ch)))
                throw new BankCardException(ErrorCodes.BankValidationFailed, "Enter an IBAN containing 15 to 34 letters or digits.");
        }
        if (bank.AccountNumber.Any(char.IsControl) || bank.GeneralLedgerAccountCode.Any(char.IsControl))
            throw new BankCardException(ErrorCodes.BankValidationFailed, "Control characters are not allowed in bank identifiers.");
    }

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50)
            throw new BankCardException(ErrorCodes.BankValidationFailed, "Enter a bank code with no more than 50 characters.");
        if (string.IsNullOrWhiteSpace(ActiveCompanyCode))
            throw new BankCardException(ErrorCodes.BankContextFailed, "Select an active company before using the bank card.");
    }

    private static async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = DatabaseRuntime.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The active database connection is not configured.");

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
            throw new BankCardException(ErrorCodes.BankContextFailed, "Select an active company before using the bank card.");

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
            throw new BankCardException(ErrorCodes.BankContextFailed, "The active company is not registered in the connected database.");
        if (IsPostgreSql)
        {
            if (value is Guid guid) return guid;
            if (Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)) return parsed;
            throw new BankCardException(ErrorCodes.BankContextFailed, "The active company identifier is invalid.");
        }
        var sqliteCompanyId = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(sqliteCompanyId)) return sqliteCompanyId;
        throw new BankCardException(ErrorCodes.BankContextFailed, "The active company identifier is invalid.");
    }

    private static void Bind(DbCommand command, BankCardRecord bank, bool includeCode = true)
    {
        if (includeCode) Add(command, "@code", bank.Code.Trim());
        Add(command, "@name", bank.Name.Trim());
        Add(command, "@accountNumber", bank.AccountNumber.Trim());
        Add(command, "@branch", bank.Branch.Trim());
        Add(command, "@iban", Regex.Replace(bank.Iban.Trim(), @"\s+", string.Empty).ToUpperInvariant());
        Add(command, "@currencyCode", bank.CurrencyCode.Trim().ToUpperInvariant());
        Add(command, "@glAccountCode", bank.GeneralLedgerAccountCode.Trim());
    }

    private static object FormatId(Guid value) =>
        IsPostgreSql ? value : value.ToString("D", CultureInfo.InvariantCulture);

    private static object TimestampValue() =>
        IsPostgreSql ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static object ActorValue()
    {
        if (!IsPostgreSql)
            return (object?)(AppState.CurrentUser?.Username) ?? DBNull.Value;
        return Guid.TryParse(AppState.CurrentUser?.Id, out var userId) && userId != Guid.Empty
            ? userId : DBNull.Value;
    }

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
