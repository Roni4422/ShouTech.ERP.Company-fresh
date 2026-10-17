#nullable enable

using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.App;

namespace ShouTech.App.Services;

/// <summary>
/// Customer card data contract. These fields match the customer-card UI and DBA migrations.
/// </summary>
public sealed record CustomerCardRecord
{
    public string Code { get; init; } = string.Empty;
    public string NameAr { get; init; } = string.Empty;
    public string NameEn { get; init; } = string.Empty;
    public int Type { get; init; } = 1;
    public string GroupCode { get; init; } = string.Empty;
    public string TaxNo { get; init; } = string.Empty;
    public string CommercialRegistration { get; init; } = string.Empty;
    public decimal CreditLimit { get; init; }
    public int CreditDays { get; init; } = 30;
    public string PriceTierCode { get; init; } = "RETAIL";
    public string GlAccountCode { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Mobile { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
}

/// <summary>A user-actionable customer-card failure with a stable catalog code.</summary>
public sealed class CustomerCardException : InvalidOperationException
{
    public string ErrorCode { get; }

    public CustomerCardException(string errorCode, string message) : base(message)
        => ErrorCode = errorCode;
}

/// <summary>
/// Provider-neutral customer-card persistence. All reads and mutations are scoped to the active
/// company; delete operations are logical deletes and never physically erase customer records.
/// </summary>
public static class CustomerCardSvc
{
    private static bool IsPostgreSql => DatabaseRuntime.IsPostgreSql();
    private static string ActiveCompanyCode =>
        AppState.CurrentCompanyCode?.Trim() ?? string.Empty;

    public static async Task<CustomerCardRecord?> LoadByCodeAsync(
        string code, CancellationToken cancellationToken = default)
    {
        ValidateCode(code);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var companyId = await ResolveCompanyIdAsync(connection, transaction: null, cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT customer_code, name_ar, name_en, type, group_code, tax_no, cr_no,
                   credit_limit, credit_days, price_tier_code, gl_account_code,
                   phone, mobile, email, address
            FROM customers
            WHERE company_id = @company AND customer_code = @code
              AND is_deleted = {FalseLiteral};
            """;
        Add(command, "@company", companyId);
        Add(command, "@code", code.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new CustomerCardRecord
        {
            Code = ReadString(reader, 0),
            NameAr = ReadString(reader, 1),
            NameEn = ReadString(reader, 2),
            Type = ReadInt32(reader, 3, 1),
            GroupCode = ReadString(reader, 4),
            TaxNo = ReadString(reader, 5),
            CommercialRegistration = ReadString(reader, 6),
            CreditLimit = ReadDecimal(reader, 7),
            CreditDays = ReadInt32(reader, 8, 30),
            PriceTierCode = ReadString(reader, 9, "RETAIL"),
            GlAccountCode = ReadString(reader, 10),
            Phone = ReadString(reader, 11),
            Mobile = ReadString(reader, 12),
            Email = ReadString(reader, 13),
            Address = ReadString(reader, 14)
        };
    }

    public static async Task SaveAsync(
        CustomerCardRecord customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        Validate(customer);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var companyId = await ResolveCompanyIdAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            object? existingId = null;
            var existingDeleted = false;

            await using (var find = connection.CreateCommand())
            {
                find.Transaction = transaction;
                find.CommandText = """
                    SELECT customer_id, is_deleted FROM customers
                    WHERE company_id = @company AND customer_code = @code;
                    """;
                Add(find, "@company", companyId);
                Add(find, "@code", customer.Code.Trim());
                await using var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    existingId = reader.GetValue(0);
                    existingDeleted = Convert.ToBoolean(reader.GetValue(1), CultureInfo.InvariantCulture);
                }
            }

            if (existingDeleted)
                throw new CustomerCardException(
                    "CUS-VAL-002",
                    "This customer code belongs to a deleted record. Restore it from the Recycle Bin before reusing the code.");

            var now = TimestampValue();
            if (existingId is null)
            {
                var newId = Guid.NewGuid();
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO customers
                    (customer_id, company_id, customer_code, name_ar, name_en, type,
                     group_code, tax_no, cr_no, credit_limit, credit_days,
                     price_tier_code, gl_account_code, phone, mobile, email, address,
                     is_active, is_deleted, created_at, created_by, modified_at, modified_by)
                    VALUES
                    (@id, @company, @code, @nameAr, @nameEn, @type,
                     @groupCode, @taxNo, @crNo, @creditLimit, @creditDays,
                     @priceTierCode, @glAccountCode, @phone, @mobile, @email, @address,
                     @isActive, @isDeleted, @createdAt, @createdBy, NULL, NULL);
                    """;
                Add(insert, "@id", FormatId(newId));
                Add(insert, "@company", companyId);
                BindCustomer(insert, customer);
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
                    UPDATE customers SET
                        name_ar=@nameAr, name_en=@nameEn, type=@type, group_code=@groupCode,
                        tax_no=@taxNo, cr_no=@crNo, credit_limit=@creditLimit, credit_days=@creditDays,
                        price_tier_code=@priceTierCode, gl_account_code=@glAccountCode,
                        phone=@phone, mobile=@mobile, email=@email, address=@address,
                        modified_at=@modifiedAt, modified_by=@modifiedBy
                    WHERE company_id=@company AND customer_id=@id AND is_deleted={FalseLiteral};
                    """;
                Add(update, "@company", companyId);
                Add(update, "@id", existingId);
                BindCustomer(update, customer);
                Add(update, "@modifiedAt", now);
                Add(update, "@modifiedBy", ActorValue());
                var affected = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (affected != 1)
                    throw new InvalidOperationException("The customer record changed during the save operation. Reload it and try again.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            AppHost.Logging?.LogOperation("CustomerCard", "Save",
                $"Company={ActiveCompanyCode}; Code={customer.Code.Trim()}; Outcome=Committed");
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
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var companyId = await ResolveCompanyIdAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE customers
                SET is_deleted={TrueLiteral}, deleted_at=@deletedAt, deleted_by=@deletedBy,
                    modified_at=@modifiedAt, modified_by=@modifiedBy
                WHERE company_id=@company AND customer_code=@code AND is_deleted={FalseLiteral};
                """;
            Add(command, "@company", companyId);
            Add(command, "@code", code.Trim());
            Add(command, "@deletedAt", TimestampValue());
            Add(command, "@deletedBy", ActorValue());
            Add(command, "@modifiedAt", TimestampValue());
            Add(command, "@modifiedBy", ActorValue());
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 1)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                AppHost.Logging?.LogOperation("CustomerCard", "SoftDelete",
                    $"Company={ActiveCompanyCode}; Code={code.Trim()}; Outcome=Committed");
                return true;
            }

            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static void Validate(CustomerCardRecord customer)
    {
        ValidateCode(customer.Code);
        if (string.IsNullOrWhiteSpace(customer.NameAr) || customer.NameAr.Trim().Length > 200)
            throw new CustomerCardException("CUS-VAL-001", "Enter an Arabic customer name with no more than 200 characters.");
        if (customer.NameEn.Trim().Length > 200)
            throw new CustomerCardException("CUS-VAL-001", "The English customer name cannot exceed 200 characters.");
        if (customer.Type is < 1 or > 3)
            throw new CustomerCardException("CUS-VAL-001", "Select a valid customer type.");
        if (customer.CreditLimit < 0 || customer.CreditLimit > 999999999999999.9999m
            || decimal.Round(customer.CreditLimit, 4) != customer.CreditLimit)
            throw new CustomerCardException("CUS-VAL-001", "The credit limit must be non-negative and support at most four decimal places.");
        if (customer.CreditDays is < 0 or > 3650)
            throw new CustomerCardException("CUS-VAL-001", "Credit days must be between 0 and 3650.");
        if (customer.GroupCode.Trim().Length > 80 || customer.PriceTierCode.Trim().Length > 30
            || customer.GlAccountCode.Trim().Length > 80)
            throw new CustomerCardException("CUS-VAL-001", "Group, price tier, and account codes exceed the supported length.");
        if (customer.TaxNo.Trim().Length > 50 || customer.CommercialRegistration.Trim().Length > 50
            || customer.Phone.Trim().Length > 30 || customer.Mobile.Trim().Length > 30
            || customer.Email.Trim().Length > 100 || customer.Address.Trim().Length > 500)
            throw new CustomerCardException("CUS-VAL-001", "One or more customer fields exceed the supported length.");
        if (!string.IsNullOrWhiteSpace(customer.Email)
            && (!customer.Email.Contains('@') || customer.Email.StartsWith('@') || customer.Email.EndsWith('@')))
            throw new CustomerCardException("CUS-VAL-001", "Enter a valid email address.");
    }

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50)
            throw new CustomerCardException("CUS-VAL-001", "Enter a customer code with no more than 50 characters.");
        if (string.IsNullOrWhiteSpace(ActiveCompanyCode))
            throw new CustomerCardException("CUS-DB-001", "Select an active company before using the customer card.");
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
            throw new CustomerCardException("CUS-DB-001", "Select an active company before using the customer card.");

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
            throw new CustomerCardException("CUS-DB-001", "The active company is not registered in the connected database.");
        if (IsPostgreSql && value is Guid guid)
            return guid;
        if (Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
            return IsPostgreSql ? parsed : parsed.ToString("D", CultureInfo.InvariantCulture);
        throw new CustomerCardException("CUS-DB-001", "The active company identifier is invalid.");
    }

    private static void BindCustomer(DbCommand command, CustomerCardRecord customer)
    {
        Add(command, "@code", customer.Code.Trim());
        Add(command, "@nameAr", customer.NameAr.Trim());
        Add(command, "@nameEn", customer.NameEn.Trim());
        Add(command, "@type", customer.Type);
        Add(command, "@groupCode", customer.GroupCode.Trim());
        Add(command, "@taxNo", customer.TaxNo.Trim());
        Add(command, "@crNo", customer.CommercialRegistration.Trim());
        Add(command, "@creditLimit", customer.CreditLimit);
        Add(command, "@creditDays", customer.CreditDays);
        Add(command, "@priceTierCode", customer.PriceTierCode.Trim());
        Add(command, "@glAccountCode", customer.GlAccountCode.Trim());
        Add(command, "@phone", customer.Phone.Trim());
        Add(command, "@mobile", customer.Mobile.Trim());
        Add(command, "@email", customer.Email.Trim());
        Add(command, "@address", customer.Address.Trim());
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
            ? userId
            : DBNull.Value;
    }

    private static string FalseLiteral => IsPostgreSql ? "FALSE" : "0";
    private static string TrueLiteral => IsPostgreSql ? "TRUE" : "1";

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string ReadString(DbDataReader reader, int ordinal, string fallback = "")
        => reader.IsDBNull(ordinal) ? fallback
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? fallback;

    private static int ReadInt32(DbDataReader reader, int ordinal, int fallback)
        => reader.IsDBNull(ordinal) ? fallback
            : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static decimal ReadDecimal(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? 0m
            : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}
